using DateCheck.Application.Dtos;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Servicos;
using DateCheck.Application.Tests.Fakes;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Application.Tests;

public class RepeticaoServiceTests
{
    private readonly FakeAgendamentoRepository _repositorio = new();
    private readonly RepeticaoService _servico;

    public RepeticaoServiceTests()
    {
        _servico = new RepeticaoService(
            _repositorio, new ConflitoService(_repositorio), TimeProvider.System);
    }

    private Agendamento Semear(DateTime inicio, int duracaoMinutos = 60, string titulo = "Academia") =>
        _repositorio.Semear(new Agendamento(titulo, inicio, inicio.AddMinutes(duracaoMinutos), "Treino"));

    private static IEnumerable<DateTime> Inicios(ResultadoRepeticao resultado) =>
        resultado.Copias.Select(c => c.Agendamento.Inicio);

    [Fact]
    public async Task Diaria_GeraUmaCopiaPorDiaAteADataFimInclusive()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 13)));

        Assert.Equal(
            [new DateTime(2026, 11, 11, 7, 0, 0), new DateTime(2026, 11, 12, 7, 0, 0), new DateTime(2026, 11, 13, 7, 0, 0)],
            Inicios(resultado));
    }

    [Fact]
    public async Task Diaria_ComIntervalo_PulaDias()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 16), Intervalo: 3));

        Assert.Equal(
            [new DateTime(2026, 11, 13, 7, 0, 0), new DateTime(2026, 11, 16, 7, 0, 0)],
            Inicios(resultado));
    }

    [Fact]
    public async Task Semanal_GeraCopiasNoMesmoDiaDaSemana()
    {
        var original = Semear(new DateTime(2026, 11, 10, 18, 30, 0));

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Semanal, new DateTime(2026, 12, 1)));

        Assert.Equal(
            [new DateTime(2026, 11, 17, 18, 30, 0), new DateTime(2026, 11, 24, 18, 30, 0), new DateTime(2026, 12, 1, 18, 30, 0)],
            Inicios(resultado));
        Assert.All(Inicios(resultado), i => Assert.Equal(DayOfWeek.Tuesday, i.DayOfWeek));
    }

    [Fact]
    public async Task Mensal_NoDia31_CaiNoUltimoDiaDosMesesCurtosEVoltaAo31()
    {
        var original = Semear(new DateTime(2026, 1, 31, 10, 0, 0));

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Mensal, new DateTime(2026, 5, 31)));

        Assert.Equal(
            [
                new DateTime(2026, 2, 28, 10, 0, 0),
                new DateTime(2026, 3, 31, 10, 0, 0),
                new DateTime(2026, 4, 30, 10, 0, 0),
                new DateTime(2026, 5, 31, 10, 0, 0)
            ],
            Inicios(resultado));
    }

    [Fact]
    public async Task Mensal_NoDia31_EmAnoBissextoFevereiroCaiNo29()
    {
        var original = Semear(new DateTime(2028, 1, 31, 10, 0, 0));

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Mensal, new DateTime(2028, 2, 29)));

        Assert.Equal([new DateTime(2028, 2, 29, 10, 0, 0)], Inicios(resultado));
    }

    [Fact]
    public async Task Mensal_ComIntervalo_PulaMeses()
    {
        var original = Semear(new DateTime(2026, 1, 15, 10, 0, 0));

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Mensal, new DateTime(2026, 7, 15), Intervalo: 3));

        Assert.Equal(
            [new DateTime(2026, 4, 15, 10, 0, 0), new DateTime(2026, 7, 15, 10, 0, 0)],
            Inicios(resultado));
    }

    [Fact]
    public async Task DataFim_ImediatamenteAntesDaProximaOcorrencia_NaoIncluiAProxima()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Semanal, new DateTime(2026, 11, 23)));

        Assert.Equal([new DateTime(2026, 11, 17, 7, 0, 0)], Inicios(resultado));
    }

    [Fact]
    public async Task Copias_MantemTituloDescricaoEDuracaoEFicamPendentes()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0), duracaoMinutos: 45);
        original.Concluir();

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 11)));

        var copia = Assert.Single(resultado.Copias).Agendamento;
        Assert.Equal("Academia", copia.Titulo);
        Assert.Equal("Treino", copia.Descricao);
        Assert.Equal(copia.Inicio.AddMinutes(45), copia.Fim);
        Assert.Equal(StatusAgendamento.Pendente, copia.Status);
    }

    [Fact]
    public async Task Repetir_AssociaOriginalECopiasAoMesmoSerieId()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 12)));

        Assert.NotEqual(Guid.Empty, resultado.SerieId);
        Assert.Equal(resultado.SerieId, original.SerieId);
        Assert.Equal(3, _repositorio.Itens.Count);
        Assert.All(_repositorio.Itens, a => Assert.Equal(resultado.SerieId, a.SerieId));
    }

    [Fact]
    public async Task Repetir_RetornaDtosComIdsDefinidosPelaGravacao()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 12)));

        Assert.All(resultado.Copias, c => Assert.True(c.Agendamento.Id > 0));
        Assert.Equal(1, _repositorio.Gravacoes);
    }

    [Fact]
    public async Task Repetir_OriginalJaEmSerie_ReaproveitaOSerieId()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));
        var serieExistente = Guid.NewGuid();
        original.AssociarSerie(serieExistente);

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 11)));

        Assert.Equal(serieExistente, resultado.SerieId);
    }

    [Fact]
    public async Task Repetir_ConflitosNaoBloqueiamEVoltamPorCopia()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));
        Semear(new DateTime(2026, 11, 11, 7, 30, 0), titulo: "Médico");

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 12)));

        Assert.Equal(2, resultado.Copias.Count);
        Assert.Equal(["Médico"], resultado.Copias[0].Conflitos.Select(c => c.Titulo));
        Assert.Empty(resultado.Copias[1].Conflitos);
        Assert.Equal(1, resultado.TotalComConflito);
        Assert.Equal(4, _repositorio.Itens.Count);
    }

    [Fact]
    public async Task Repetir_ConflitoIgnoraCanceladosEHorariosEncostados()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));
        var cancelado = Semear(new DateTime(2026, 11, 11, 7, 0, 0), titulo: "Cancelado");
        cancelado.Cancelar();
        Semear(new DateTime(2026, 11, 11, 8, 0, 0), titulo: "Encostado");

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 11)));

        Assert.Empty(resultado.Copias[0].Conflitos);
    }

    [Fact]
    public async Task Previsualizar_NaoSalvaNemAlteraOOriginal()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));
        Semear(new DateTime(2026, 11, 11, 7, 0, 0), titulo: "Médico");

        var resultado = await _servico.PrevisualizarAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 12)));

        Assert.Equal(2, resultado.Copias.Count);
        Assert.Equal(1, resultado.TotalComConflito);
        Assert.Equal(2, _repositorio.Itens.Count);
        Assert.Equal(0, _repositorio.Gravacoes);
        Assert.Null(original.SerieId);
    }

    [Fact]
    public async Task DataFim_ExatamenteUmAnoDepois_EhPermitida()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));

        var resultado = await _servico.PrevisualizarAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Mensal, new DateTime(2027, 11, 10)));

        Assert.Equal(12, resultado.Copias.Count);
    }

    [Fact]
    public async Task DataFim_UmDiaAlemDoLimiteDeUmAno_LancaAplicacaoException()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));

        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2027, 11, 11))));

        Assert.Equal(0, _repositorio.Gravacoes);
    }

    [Fact]
    public async Task DataFim_AnteriorAPrimeiraCopia_LancaAplicacaoException()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));

        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Semanal, new DateTime(2026, 11, 12))));

        Assert.Equal(0, _repositorio.Gravacoes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(367)]
    [InlineData(int.MaxValue)]
    public async Task Intervalo_ForaDoPermitido_LancaAplicacaoException(int intervalo)
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));

        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Mensal, new DateTime(2027, 1, 10), intervalo)));
    }

    [Fact]
    public async Task Frequencia_Invalida_LancaAplicacaoException()
    {
        var original = Semear(new DateTime(2026, 11, 10, 7, 0, 0));

        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.RepetirAsync(
            new RepetirRequest(original.Id, (FrequenciaRepeticao)99, new DateTime(2026, 11, 12))));
    }

    [Fact]
    public async Task Diaria_ComDuracaoDeDoisDias_LancaAplicacaoExceptionENaoSalva()
    {
        var original = Semear(new DateTime(2026, 11, 10, 8, 0, 0), duracaoMinutos: 2 * 24 * 60);
        var request = new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 14));

        var excecao = await Assert.ThrowsAsync<AplicacaoException>(() => _servico.RepetirAsync(request));
        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.PrevisualizarAsync(request));

        Assert.Contains("as cópias se sobreporiam", excecao.Message);
        Assert.Equal(0, _repositorio.Gravacoes);
        Assert.Single(_repositorio.Itens);
        Assert.Null(original.SerieId);
    }

    [Fact]
    public async Task Diaria_ComDuracaoDeExatamente24h_EhAceitaPoisAsCopiasSoEncostam()
    {
        var original = Semear(new DateTime(2026, 11, 10, 8, 0, 0), duracaoMinutos: 24 * 60);

        var resultado = await _servico.RepetirAsync(
            new RepetirRequest(original.Id, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 12)));

        Assert.Equal(2, resultado.Copias.Count);
        Assert.Equal(original.Fim, resultado.Copias[0].Agendamento.Inicio);
    }

    [Fact]
    public async Task Mensal_PartindoDe31DeJaneiroComDuracaoMaiorQue28Dias_LancaAplicacaoException()
    {
        var original = Semear(new DateTime(2026, 1, 31, 8, 0, 0), duracaoMinutos: 30 * 24 * 60);
        var request = new RepetirRequest(original.Id, FrequenciaRepeticao.Mensal, new DateTime(2026, 4, 30));

        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.RepetirAsync(request));
        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.PrevisualizarAsync(request));
        Assert.Equal(0, _repositorio.Gravacoes);
    }

    [Fact]
    public async Task Repetir_Inexistente_LancaAgendamentoNaoEncontrado()
    {
        await Assert.ThrowsAsync<AgendamentoNaoEncontradoException>(() => _servico.RepetirAsync(
            new RepetirRequest(99, FrequenciaRepeticao.Diaria, new DateTime(2026, 11, 12))));
    }
}
