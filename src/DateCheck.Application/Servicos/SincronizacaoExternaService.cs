using DateCheck.Application.Dtos;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace DateCheck.Application.Servicos;

/// <summary>
/// Sincronização de mão única (origem externa → DateCheck). Regras de segurança, todas testadas:
/// <list type="bullet">
/// <item>A lista da fonte é completa ou a chamada falha; em qualquer falha NADA é removido nem alterado.</item>
/// <item>Corte exato: só entra (inserir, atualizar ou remover) o que tem <c>Inicio</c> local em [início, fim) da
/// janela; o que a fonte trouxer na folga de fuso, fora da janela, é descartado antes de tocar no banco.</item>
/// <item>Nunca duas sincronizações ao mesmo tempo.</item>
/// <item>401 vira "Reconecte o Notion" e o segundo plano para de insistir.</item>
/// </list>
/// </summary>
public sealed class SincronizacaoExternaService : ISincronizacaoExternaService
{
    private readonly IFonteDeAgendamentosExterna _fonte;
    private readonly IAgendamentoRepository _repositorio;
    private readonly TimeProvider _relogio;
    private readonly ILogger<SincronizacaoExternaService> _logger;

    private readonly SemaphoreSlim _trava = new(1, 1);
    private volatile EstadoDaSincronizacao _estado = EstadoDaSincronizacao.NaoConfigurado;

    public SincronizacaoExternaService(
        IFonteDeAgendamentosExterna fonte,
        IAgendamentoRepository repositorio,
        TimeProvider relogio,
        ILogger<SincronizacaoExternaService> logger)
    {
        _fonte = fonte;
        _repositorio = repositorio;
        _relogio = relogio;
        _logger = logger;
    }

    public EstadoDaSincronizacao Estado => _estado;

    public event Action<EstadoDaSincronizacao>? EstadoAlterado;

    public event Action? DadosAlterados;

    public event Action? ReconexaoNecessaria;

    public async Task<EstadoDaSincronizacao> SincronizarAsync(bool manual = false, CancellationToken cancellationToken = default)
    {
        // Nunca duas ao mesmo tempo: quem chega com uma em andamento não espera na fila, só vê o estado atual.
        if (!await _trava.WaitAsync(TimeSpan.Zero, cancellationToken))
            return _estado;

        try
        {
            return await ExecutarAsync(manual, cancellationToken);
        }
        finally
        {
            _trava.Release();
        }
    }

    private async Task<EstadoDaSincronizacao> ExecutarAsync(bool manual, CancellationToken cancellationToken)
    {
        if (!_fonte.EstaConfigurada)
            return Definir(EstadoDaSincronizacao.NaoConfigurado);

        if (!manual && _estado.Situacao == SituacaoDaSincronizacao.ReconecteNecessario)
            return _estado; // o token foi recusado: só o usuário resolve (reconectando ou pedindo "Sincronizar agora")

        var anterior = _estado;
        Definir(anterior with { Situacao = SituacaoDaSincronizacao.Sincronizando });

        var agora = _relogio.GetLocalNow().DateTime;
        var janela = JanelaDeSincronizacao.Calcular(agora);

        try
        {
            var recebidos = await _fonte.ListarAsync(janela.Inicio, janela.Fim, cancellationToken);

            // Corte exato: o que a fonte trouxe de folga fora da janela não entra em nada (nem inserir, nem
            // atualizar, nem poupar ou remover). Repetidos (a paginação pode repetir uma página que mudou) valem uma vez.
            var dentroDaJanela = recebidos
                .Where(item => janela.Contem(item.Inicio))
                .GroupBy(item => item.IdExterno, StringComparer.OrdinalIgnoreCase)
                .Select(grupo => grupo.Last())
                .ToList();

            var resultado = await _repositorio.SincronizarAsync(
                _fonte.Origem, janela.Inicio, janela.Fim, dentroDaJanela, cancellationToken);

            var novo = Definir(new EstadoDaSincronizacao(
                SituacaoDaSincronizacao.Ok, _relogio.GetLocalNow().DateTime, Erro: null, resultado));

            _logger.LogInformation(
                "Sincronização com {Origem}: {Inseridos} inseridos, {Atualizados} atualizados, {Removidos} removidos.",
                _fonte.Origem, resultado.Inseridos, resultado.Atualizados, resultado.Removidos);

            if (resultado.Total > 0)
                Avisar(() => DadosAlterados?.Invoke());

            return novo;
        }
        catch (OperationCanceledException)
        {
            Definir(anterior); // não deixa o estado preso em "Sincronizando"
            throw;
        }
        catch (FonteExternaException ex)
        {
            // Nenhuma lista parcial chega ao repositório: o banco local fica exatamente como estava.
            // A mensagem é a mesma que aparece nas Configurações e nunca contém segredos (ver FonteExternaException).
            _logger.LogWarning("Falha na sincronização com {Origem}: {Tipo}. {Mensagem}", _fonte.Origem, ex.Tipo, ex.Message);
            if (ex.CausaOriginal is not null)
                _logger.LogDebug(ex.CausaOriginal, "Causa da falha de sincronização.");

            var primeiraVez = ex.ExigeReconexao && anterior.Situacao != SituacaoDaSincronizacao.ReconecteNecessario;
            var situacao = ex.ExigeReconexao ? SituacaoDaSincronizacao.ReconecteNecessario : SituacaoDaSincronizacao.Erro;
            var novo = Definir(anterior with { Situacao = situacao, Erro = ex.Message });

            if (primeiraVez)
                Avisar(() => ReconexaoNecessaria?.Invoke());

            return novo;
        }
        catch (Exception ex)
        {
            // Banco indisponível, por exemplo: registra e segue; a próxima rodada tenta de novo.
            _logger.LogError(ex, "Erro inesperado na sincronização com {Origem}.", _fonte.Origem);
            return Definir(anterior with
            {
                Situacao = SituacaoDaSincronizacao.Erro,
                Erro = "Não foi possível gravar os compromissos importados. Veja o log do aplicativo."
            });
        }
    }

    public void Reiniciar()
    {
        Definir(_fonte.EstaConfigurada
            ? new EstadoDaSincronizacao(SituacaoDaSincronizacao.Aguardando)
            : EstadoDaSincronizacao.NaoConfigurado);
    }

    public async Task<TResultado> ExecutarSemSincronizacaoAsync<TResultado>(
        Func<Task<TResultado>> acao, CancellationToken cancellationToken = default)
    {
        await _trava.WaitAsync(cancellationToken);
        try
        {
            return await acao();
        }
        finally
        {
            _trava.Release();
        }
    }

    private EstadoDaSincronizacao Definir(EstadoDaSincronizacao novo)
    {
        _estado = novo;
        Avisar(() => EstadoAlterado?.Invoke(novo));
        return novo;
    }

    /// <summary>Um ouvinte com defeito não pode derrubar a sincronização.</summary>
    private void Avisar(Action evento)
    {
        try
        {
            evento();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Um ouvinte de evento da sincronização falhou.");
        }
    }
}
