using DateCheck.Application.Dtos;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Interfaces;
using DateCheck.Domain.Entidades;

namespace DateCheck.Application.Servicos;

public sealed class RepeticaoService : IRepeticaoService
{
    // Como a repetição vai até 1 ano, intervalos maiores que isso nunca gerariam uma segunda cópia.
    private const int LimiteIntervalo = 366;

    private readonly IAgendamentoRepository _repositorio;
    private readonly IConflitoService _conflitos;
    private readonly TimeProvider _relogio;

    public RepeticaoService(
        IAgendamentoRepository repositorio, IConflitoService conflitos, TimeProvider relogio)
    {
        _repositorio = repositorio;
        _conflitos = conflitos;
        _relogio = relogio;
    }

    public async Task<ResultadoRepeticao> PrevisualizarAsync(
        RepetirRequest request, CancellationToken cancellationToken = default)
    {
        var (_, _, resultado) = await GerarAsync(request, cancellationToken);
        return resultado;
    }

    public async Task<ResultadoRepeticao> RepetirAsync(
        RepetirRequest request, CancellationToken cancellationToken = default)
    {
        var (original, copias, resultado) = await GerarAsync(request, cancellationToken);

        original.AssociarSerie(resultado.SerieId);
        await _repositorio.AdicionarSerieAsync(original, copias, cancellationToken);

        // Devolve os DTOs já com os Ids definidos pela gravação.
        var agora = _relogio.GetLocalNow().DateTime;
        var gravadas = resultado.Copias
            .Select((c, i) => c with { Agendamento = AgendamentoMapper.ParaDto(copias[i], agora) })
            .ToList();

        return resultado with { Copias = gravadas };
    }

    private async Task<(Agendamento Original, List<Agendamento> Copias, ResultadoRepeticao Resultado)> GerarAsync(
        RepetirRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Frequencia))
            throw new AplicacaoException("Frequência de repetição inválida.");

        if (request.Intervalo is < 1 or > LimiteIntervalo)
            throw new AplicacaoException($"O intervalo da repetição deve estar entre 1 e {LimiteIntervalo}.");

        var original = await _repositorio.ObterPorIdAsync(request.AgendamentoId, cancellationToken)
            ?? throw new AgendamentoNaoEncontradoException(request.AgendamentoId);

        var dataFim = request.DataFim.Date;
        if (dataFim > original.Inicio.Date.AddYears(1))
            throw new AplicacaoException("A repetição pode se estender por no máximo 1 ano.");

        var serieId = original.SerieId ?? Guid.NewGuid();
        var copias = new List<Agendamento>();

        for (var passo = 1; ; passo++)
        {
            var inicio = CalcularInicio(original.Inicio, request.Frequencia, request.Intervalo, passo);
            if (inicio.Date > dataFim)
                break;

            copias.Add(original.CopiarPara(inicio, serieId));
        }

        if (copias.Count == 0)
            throw new AplicacaoException("A data de fim não permite gerar nenhuma cópia.");

        GarantirQueCopiasNaoSeSobrepoem(original, copias);

        var agora = _relogio.GetLocalNow().DateTime;
        var geradas = new List<CopiaGerada>(copias.Count);
        foreach (var copia in copias)
        {
            var conflitos = await _conflitos.BuscarConflitosAsync(
                copia.Inicio, copia.Fim, cancellationToken: cancellationToken);
            geradas.Add(new CopiaGerada(AgendamentoMapper.ParaDto(copia, agora), conflitos));
        }

        return (original, copias, new ResultadoRepeticao(serieId, geradas));
    }

    /// <summary>
    /// Confere os pares vizinhos (original, 1ª cópia, 2ª cópia...). Bordas encostadas (fim == próximo início) são permitidas.
    /// </summary>
    private static void GarantirQueCopiasNaoSeSobrepoem(Agendamento original, IReadOnlyList<Agendamento> copias)
    {
        var anterior = original;
        foreach (var copia in copias)
        {
            if (copia.Inicio < anterior.Fim)
                throw new AplicacaoException(
                    "A duração do agendamento é maior que o intervalo da repetição; as cópias se sobreporiam.");

            anterior = copia;
        }
    }

    /// <summary>
    /// Sempre calcula a partir do início do original (e não da cópia anterior), para que um dia 31
    /// caia no último dia dos meses curtos e volte ao dia 31 nos meses longos seguintes.
    /// </summary>
    private static DateTime CalcularInicio(DateTime inicioOriginal, FrequenciaRepeticao frequencia, int intervalo, int passo) =>
        frequencia switch
        {
            FrequenciaRepeticao.Diaria => inicioOriginal.AddDays((double)intervalo * passo),
            FrequenciaRepeticao.Semanal => inicioOriginal.AddDays(7d * intervalo * passo),
            FrequenciaRepeticao.Mensal => inicioOriginal.AddMonths(intervalo * passo),
            _ => throw new AplicacaoException("Frequência de repetição inválida.")
        };
}
