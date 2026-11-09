using DateCheck.Application.Dtos;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Servicos;
using DateCheck.Application.Tests.Fakes;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
using DateCheck.Domain.Exceptions;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Application.Tests;

/// <summary>Como os serviços tratam itens externos (Notion) e de dia inteiro.</summary>
public class OrigemExternaServicosTests
{
    private static readonly DateTime Hoje = new(2026, 11, 10);

    private readonly FakeAgendamentoRepository _repositorio = new();
    private readonly FakeTimeProvider _relogio = new();
    private readonly AgendamentoService _agendamentos;
    private readonly ConflitoService _conflitos;
    private readonly DuplicacaoService _duplicacao;
    private readonly RepeticaoService _repeticao;

    public OrigemExternaServicosTests()
    {
        _relogio.SetLocalTimeZone(TimeZoneInfo.Utc);
        DefinirAgora(Hoje.AddHours(12));
        _agendamentos = new AgendamentoService(_repositorio, _relogio);
        _conflitos = new ConflitoService(_repositorio);
        _duplicacao = new DuplicacaoService(_repositorio, _relogio);
        _repeticao = new RepeticaoService(_repositorio, _conflitos, _relogio);
    }

    private void DefinirAgora(DateTime agora) =>
        _relogio.SetUtcNow(new DateTimeOffset(DateTime.SpecifyKind(agora, DateTimeKind.Utc)));

    private static DateTime H(int hora, int minuto = 0, int diasDepois = 0) =>
        Hoje.AddDays(diasDepois).AddHours(hora).AddMinutes(minuto);

    private Agendamento SemearLocal(string titulo, DateTime inicio, DateTime fim) =>
        _repositorio.Semear(new Agendamento(titulo, inicio, fim));

    private Agendamento SemearNotion(
        string titulo, DateTime inicio, DateTime fim, bool diaInteiro = false,
        StatusAgendamento status = StatusAgendamento.Pendente, string? id = null) =>
        _repositorio.Semear(Agendamento.Importar(
            OrigemAgendamento.Notion, id ?? $"id-{titulo}", $"https://www.notion.so/{titulo}", titulo, null,
            inicio, fim, diaInteiro, status));

    private Agendamento SemearDiaInteiro(string titulo, int diasDepois = 0, int dias = 1) =>
        SemearNotion(titulo, Hoje.AddDays(diasDepois), Hoje.AddDays(diasDepois + dias), diaInteiro: true);

    // ---------- conflitos ----------

    [Fact]
    public async Task Conflito_ItemDeDiaInteiroNaoConflitaComNada()
    {
        SemearDiaInteiro("Férias", dias: 3);

        var conflitos = await _conflitos.BuscarConflitosAsync(H(9), H(10));

        Assert.Empty(conflitos);
    }

    [Fact]
    public async Task Conflito_ConsultaDeDiaInteiroNaoConflitaComNada()
    {
        SemearLocal("Reunião", H(9), H(10));

        var conflitos = await _conflitos.BuscarConflitosAsync(Hoje, Hoje.AddDays(1), diaInteiro: true);

        Assert.Empty(conflitos);
    }

    [Fact]
    public async Task Conflito_ItemExternoComHorarioConflitaComoQualquerOutro()
    {
        SemearNotion("Dentista", H(9), H(10));

        var conflitos = await _conflitos.BuscarConflitosAsync(H(9, 30), H(10, 30));

        var conflito = Assert.Single(conflitos);
        Assert.Equal("Dentista", conflito.Titulo);
        Assert.Equal(OrigemAgendamento.Notion, conflito.Origem);
    }

    [Fact]
    public async Task Conflito_DiaInteiroNaoEscondeConflitoRealNoMesmoDia()
    {
        SemearDiaInteiro("Feriado");
        SemearLocal("Reunião", H(9), H(10));

        var conflitos = await _conflitos.BuscarConflitosAsync(H(9, 30), H(10, 30));

        Assert.Equal(["Reunião"], conflitos.Select(c => c.Titulo));
    }

    // ---------- duplicar e repetir ----------

    [Fact]
    public async Task Duplicar_ItemExterno_ERecusadoComMensagemClaraENaoGrava()
    {
        var externo = SemearNotion("Dentista", H(9), H(10));
        var antes = _repositorio.Itens.Count;

        var ex = await Assert.ThrowsAsync<AplicacaoException>(() => _duplicacao.DuplicarAsync(externo.Id, H(9, diasDepois: 1)));

        Assert.Contains("Notion", ex.Message);
        Assert.Contains("duplicados", ex.Message);
        Assert.Equal(antes, _repositorio.Itens.Count);
        Assert.Equal(0, _repositorio.Gravacoes);
    }

    [Fact]
    public async Task Repetir_ItemExterno_ERecusadoEmPreviaEEmGravacao()
    {
        var externo = SemearNotion("Dentista", H(9), H(10));
        var request = new RepetirRequest(externo.Id, FrequenciaRepeticao.Semanal, Hoje.AddDays(30));

        var previa = await Assert.ThrowsAsync<AplicacaoException>(() => _repeticao.PrevisualizarAsync(request));
        var grava = await Assert.ThrowsAsync<AplicacaoException>(() => _repeticao.RepetirAsync(request));

        Assert.Contains("repetidos", previa.Message);
        Assert.Contains("Notion", grava.Message);
        Assert.Single(_repositorio.Itens);
        Assert.Equal(0, _repositorio.Gravacoes);
    }

    // ---------- somente leitura nos serviços ----------

    [Fact]
    public async Task Concluir_Cancelar_Reabrir_ItemExterno_LancamDomainExceptionENaoGravam()
    {
        var externo = SemearNotion("Dentista", H(15), H(16));

        await Assert.ThrowsAsync<DomainException>(() => _agendamentos.ConcluirAsync(externo.Id));
        await Assert.ThrowsAsync<DomainException>(() => _agendamentos.CancelarAsync(externo.Id));
        await Assert.ThrowsAsync<DomainException>(() => _agendamentos.ReabrirAsync(externo.Id));

        Assert.Equal(StatusAgendamento.Pendente, externo.Status);
        Assert.Equal(0, _repositorio.Gravacoes);
    }

    [Fact]
    public async Task Editar_ItemExterno_LancaDomainExceptionENaoAlteraNada()
    {
        var externo = SemearNotion("Dentista", H(15), H(16));

        var ex = await Assert.ThrowsAsync<DomainException>(() => _agendamentos.EditarAsync(
            new EditarAgendamentoRequest(externo.Id, "Outro", H(17), H(18))));

        Assert.Equal("Este compromisso vem do Notion; edite-o lá.", ex.Message);
        Assert.Equal("Dentista", externo.Titulo);
        Assert.Equal(H(15), externo.Inicio);
    }

    // ---------- DTO ----------

    [Fact]
    public async Task Dto_TrazOrigemUrlEDiaInteiro()
    {
        var externo = SemearNotion("Férias", Hoje, Hoje.AddDays(2), diaInteiro: true);
        SemearLocal("Reunião", H(9), H(10));

        var lista = await _agendamentos.ListarDoDiaAsync(Hoje);

        var ferias = lista.Single(a => a.Id == externo.Id);
        Assert.Equal(OrigemAgendamento.Notion, ferias.Origem);
        Assert.Equal("https://www.notion.so/Férias", ferias.UrlExterna);
        Assert.True(ferias.DiaInteiro);
        Assert.True(ferias.EhExterno);

        var reuniao = lista.Single(a => a.Titulo == "Reunião");
        Assert.Equal(OrigemAgendamento.Local, reuniao.Origem);
        Assert.Null(reuniao.UrlExterna);
        Assert.False(reuniao.DiaInteiro);
        Assert.False(reuniao.EhExterno);
    }

    [Fact]
    public async Task Obter_ItemExterno_TrazOrigem()
    {
        var externo = SemearNotion("Dentista", H(15), H(16));

        var dto = await _agendamentos.ObterAsync(externo.Id);

        Assert.Equal(OrigemAgendamento.Notion, dto.Origem);
        Assert.Equal(externo.UrlExterna, dto.UrlExterna);
    }

    // ---------- ordenação e destaques da tela Hoje ----------

    [Fact]
    public async Task ListarDoDia_DiaInteiroVemPrimeiroDepoisPorHorario()
    {
        SemearLocal("Tarde", H(15), H(16));
        SemearLocal("Meia-noite", H(0), H(0, 30));
        SemearDiaInteiro("Feriado");
        SemearLocal("Manhã", H(9), H(10));
        SemearDiaInteiro("Férias", diasDepois: -2, dias: 4); // começou antes, ainda ocupa hoje

        var lista = await _agendamentos.ListarDoDiaAsync(Hoje);

        Assert.Equal(["Férias", "Feriado", "Meia-noite", "Manhã", "Tarde"], lista.Select(a => a.Titulo));
    }

    [Fact]
    public async Task ListarDoDia_DiaInteiroNuncaEhProximoNemAtrasado()
    {
        DefinirAgora(H(12));
        SemearDiaInteiro("Feriado");
        var reuniao = SemearLocal("Reunião", H(15), H(16));

        var lista = await _agendamentos.ListarDoDiaAsync(Hoje);

        var feriado = lista.Single(a => a.Titulo == "Feriado");
        Assert.False(feriado.Proximo);
        Assert.False(feriado.Atrasado);
        Assert.True(lista.Single(a => a.Id == reuniao.Id).Proximo);
    }

    [Fact]
    public async Task ListarDoDia_DiaInteiroDeOntemNaoFicaAtrasadoNemQuandoJaPassou()
    {
        SemearDiaInteiro("Feriado de ontem", diasDepois: -1);
        DefinirAgora(H(12));

        var lista = await _agendamentos.ListarPeriodoAsync(Hoje.AddDays(-1), Hoje.AddDays(2));

        Assert.False(lista.Single().Atrasado);
    }

    [Fact]
    public async Task ListarDoDia_SoHaDiaInteiro_NaoTemProximo()
    {
        SemearDiaInteiro("Feriado");

        var lista = await _agendamentos.ListarDoDiaAsync(Hoje);

        Assert.DoesNotContain(lista, a => a.Proximo);
    }

    [Fact]
    public async Task ListarDoDia_ItemExternoComHorarioCompetePeloProximoComoQualquerOutro()
    {
        DefinirAgora(H(12));
        SemearLocal("Depois", H(18), H(19));
        var externo = SemearNotion("Dentista", H(15), H(16));

        var lista = await _agendamentos.ListarDoDiaAsync(Hoje);

        Assert.True(lista.Single(a => a.Id == externo.Id).Proximo);
    }

    [Fact]
    public async Task ListarDoDia_DiaInteiroDeVariosDiasApareceEmCadaDiaMasNaoNoSeguinteAoFim()
    {
        SemearDiaInteiro("Viagem", diasDepois: 0, dias: 2); // hoje e amanhã

        Assert.Single(await _agendamentos.ListarDoDiaAsync(Hoje));
        Assert.Single(await _agendamentos.ListarDoDiaAsync(Hoje.AddDays(1)));
        Assert.Empty(await _agendamentos.ListarDoDiaAsync(Hoje.AddDays(2)));
    }

    [Fact]
    public async Task ListarPeriodo_DiaInteiroTambemVemPrimeiro()
    {
        SemearLocal("Meia-noite", H(0), H(0, 30));
        SemearDiaInteiro("Feriado");

        var lista = await _agendamentos.ListarPeriodoAsync(Hoje, Hoje.AddDays(1));

        Assert.Equal(["Feriado", "Meia-noite"], lista.Select(a => a.Titulo));
    }
}
