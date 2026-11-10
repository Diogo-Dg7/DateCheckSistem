using System.Text.Json;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Notion;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
using DateCheck.Integracoes.Notion;
using DateCheck.Integracoes.Tests.Apoio;

namespace DateCheck.Integracoes.Tests;

public class ConversorDePaginaNotionTests
{
    // Máquina "local" fictícia em UTC-3 (sem horário de verão), para os resultados não dependerem de onde o teste roda.
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.CreateCustomTimeZone("Teste-03", TimeSpan.FromHours(-3), "Teste", "Teste");

    private static readonly ConfiguracaoNotion Config = new("db", "ds-0001", "Data");

    private readonly LogCapturado<ConversorDePaginaNotion> _log = new();

    private ConversorDePaginaNotion CriarConversor(TimeZoneInfo? fuso = null) => new(_log, fuso ?? Fuso);

    private Agendamento? Converter(string arquivo, ConfiguracaoNotion? config = null, TimeZoneInfo? fuso = null) =>
        CriarConversor(fuso).Converter(Exemplos.Json(arquivo), config ?? Config);

    private Agendamento ConverterOk(string arquivo, ConfiguracaoNotion? config = null, TimeZoneInfo? fuso = null) =>
        Converter(arquivo, config, fuso) ?? throw new Xunit.Sdk.XunitException($"{arquivo} deveria gerar um agendamento.");

    // ---------- tabela de datas ----------

    [Fact]
    public void DataSemHorarioSemFim_EDiaInteiroDeUmDia()
    {
        var item = ConverterOk("pagina-data-sem-horario.json");

        Assert.True(item.DiaInteiro);
        Assert.Equal(new DateTime(2026, 11, 15), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 16), item.Fim);
    }

    [Fact]
    public void DataSemHorarioComFim_EDiaInteiroAteOFimInclusive()
    {
        var item = ConverterOk("pagina-data-intervalo-sem-horario.json"); // 10/11 a 12/11

        Assert.True(item.DiaInteiro);
        Assert.Equal(new DateTime(2026, 11, 10), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 13), item.Fim); // meia-noite do dia seguinte ao último
    }

    [Fact]
    public void DataComHorarioEFim_ConverteInicioEFim()
    {
        var item = ConverterOk("pagina-horario-com-fim.json");

        Assert.False(item.DiaInteiro);
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 10, 10, 30, 0), item.Fim);
    }

    [Fact]
    public void DataComHorarioSemFim_TemDuracaoPadraoDeUmaHora()
    {
        var item = ConverterOk("pagina-horario-sem-fim.json");

        Assert.Equal(new DateTime(2026, 11, 10, 14, 0, 0), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 10, 15, 0, 0), item.Fim);
        Assert.Equal(TimeSpan.FromHours(1), ConversorDePaginaNotion.DuracaoPadrao);
    }

    [Fact]
    public void FimAntesDoInicio_UsaDuracaoPadraoEAvisaNoLog()
    {
        var item = ConverterOk("pagina-fim-antes-do-inicio.json");

        Assert.Equal(new DateTime(2026, 11, 10, 14, 0, 0), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 10, 15, 0, 0), item.Fim);
        var aviso = Assert.Single(_log.Avisos);
        Assert.Contains("00000000-0000-0000-0000-000000000005", aviso);
        Assert.DoesNotContain("Dado inconsistente", aviso); // o título pode ser privado: só o id vai para o log
    }

    [Fact]
    public void SemAData_EIgnorada()
    {
        Assert.Null(Converter("pagina-sem-data.json"));
    }

    // ---------- fusos ----------

    [Fact]
    public void DataEmUtc_ConvertidaParaOHorarioLocal()
    {
        var item = ConverterOk("pagina-fuso-utc.json"); // 12:00Z–13:00Z

        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 10, 10, 0, 0), item.Fim);
    }

    [Fact]
    public void DataComOffsetDiferenteDoLocal_ConvertidaEPodeMudarODia()
    {
        var item = ConverterOk("pagina-fuso-positivo.json"); // 09:00+09:00 = 00:00Z = 21:00 do dia 9 em UTC-3

        Assert.Equal(new DateTime(2026, 11, 9, 21, 0, 0), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 9, 23, 0, 0), item.Fim);
    }

    [Fact]
    public void MesmoInstanteEmOutroFusoLocal_DaOutroHorario()
    {
        var item = ConverterOk("pagina-horario-com-fim.json", fuso: TimeZoneInfo.Utc); // 09:00-03:00 = 12:00Z

        Assert.Equal(new DateTime(2026, 11, 10, 12, 0, 0), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 10, 13, 30, 0), item.Fim);
    }

    [Fact]
    public void HorarioSemOffsetComTimeZone_UsaOFusoInformado()
    {
        var item = ConverterOk("pagina-timezone-sem-offset.json"); // 09:00 em Nova York (EST, UTC-5) = 11:00 em UTC-3

        Assert.Equal(new DateTime(2026, 11, 10, 11, 0, 0), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 10, 12, 0, 0), item.Fim);
    }

    [Fact]
    public void HorarioSemOffsetESemTimeZone_ValeComoHorarioLocal()
    {
        var item = ConverterOk("pagina-horario-flutuante.json");

        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 10, 10, 0, 0), item.Fim);
    }

    [Fact]
    public void DiaInteiro_NaoSofreConversaoDeFuso()
    {
        foreach (var fuso in new[] { TimeZoneInfo.Utc, Fuso, TimeZoneInfo.CreateCustomTimeZone("mais14", TimeSpan.FromHours(14), "x", "x") })
        {
            var item = ConverterOk("pagina-data-sem-horario.json", fuso: fuso);

            Assert.Equal(new DateTime(2026, 11, 15), item.Inicio);
        }
    }

    // ---------- título, ids e status ----------

    [Fact]
    public void CopiaIdEUrlDaPaginaEOrigemNotionSemDescricao()
    {
        var item = ConverterOk("pagina-horario-com-fim.json");

        Assert.Equal("00000000-0000-0000-0000-000000000003", item.IdExterno);
        Assert.Equal("https://www.notion.so/00000000000000000000000000000003", item.UrlExterna);
        Assert.Equal(OrigemAgendamento.Notion, item.Origem);
        Assert.Equal("Dentista", item.Titulo);
        Assert.Null(item.Descricao);
    }

    [Fact]
    public void TituloVazio_ViraSemTitulo()
    {
        Assert.Equal("(sem título)", ConverterOk("pagina-sem-titulo.json").Titulo);
    }

    [Fact]
    public void TituloEmVariasPartes_EConcatenado()
    {
        Assert.Equal("Reunião de planejamento", ConverterOk("pagina-titulo-em-partes.json").Titulo);
    }

    [Fact]
    public void TituloAcimaDoLimite_ETruncado()
    {
        var item = ConverterOk("pagina-titulo-gigante.json");

        Assert.Equal(Agendamento.TamanhoMaximoTitulo, item.Titulo.Length);
    }

    [Fact]
    public void Truncar_NaoPartePareSubstitutoEAparaEspacos()
    {
        var comEmoji = new string('a', 199) + "😀😀";

        var truncado = ConversorDePaginaNotion.Truncar(comEmoji, 200);

        Assert.Equal(199, truncado.Length); // o emoji (2 chars) não cabe inteiro: sai por inteiro
        Assert.Equal("curto", ConversorDePaginaNotion.Truncar("  curto  ", 200));
        Assert.Equal("ab", ConversorDePaginaNotion.Truncar("ab    c", 4)); // sem espaço sobrando no fim
        Assert.Equal(string.Empty, ConversorDePaginaNotion.Truncar(null, 10));
    }

    [Fact]
    public void SemPropriedadeDeConclusaoConfigurada_TudoEntraPendente()
    {
        Assert.Equal(StatusAgendamento.Pendente, ConverterOk("pagina-concluida-checkbox.json").Status);
    }

    [Theory]
    [InlineData("pagina-concluida-checkbox.json", StatusAgendamento.Concluido)]
    [InlineData("pagina-pendente-checkbox.json", StatusAgendamento.Pendente)]
    public void Checkbox_MarcadoEConcluido(string arquivo, StatusAgendamento esperado)
    {
        var config = Config with { Conclusao = new ConclusaoNotion("Feito", TipoDeConclusaoNotion.Checkbox) };

        Assert.Equal(esperado, ConverterOk(arquivo, config).Status);
    }

    [Theory]
    [InlineData("pagina-status-concluido.json", "Concluído", StatusAgendamento.Concluido)]
    [InlineData("pagina-status-concluido.json", "concluído", StatusAgendamento.Concluido)]
    [InlineData("pagina-status-concluido.json", "Em andamento", StatusAgendamento.Pendente)]
    [InlineData("pagina-status-em-andamento.json", "Concluído", StatusAgendamento.Pendente)]
    [InlineData("pagina-status-em-andamento.json", "Em andamento", StatusAgendamento.Concluido)]
    [InlineData("pagina-status-vazio.json", "Concluído", StatusAgendamento.Pendente)]
    public void Status_SoOValorConfiguradoSignificaConcluido(string arquivo, string valorConcluido, StatusAgendamento esperado)
    {
        var config = Config with { Conclusao = new ConclusaoNotion("Status", TipoDeConclusaoNotion.Status, valorConcluido) };

        Assert.Equal(esperado, ConverterOk(arquivo, config).Status);
    }

    // ---------- lixeira ----------

    [Theory]
    [InlineData("pagina-na-lixeira.json")]
    [InlineData("pagina-arquivada-api-antiga.json")]
    public void PaginasNaLixeiraOuArquivadas_NaoEntram(string arquivo)
    {
        Assert.Null(Converter(arquivo));
    }

    [Fact]
    public void Lixeira_ReconhecidaSoComInTrash_SemOCampoArchived()
    {
        var json = """{"id":"p1","in_trash":true,"properties":{"Data":{"type":"date","date":{"start":"2026-11-10"}}}}""";

        Assert.Null(CriarConversor().Converter(JsonDocument.Parse(json).RootElement, Config));
    }

    // ---------- configuração desatualizada: falha em vez de ignorar tudo ----------

    [Fact]
    public void PropriedadeDeDataRenomeada_FalhaEmVezDeIgnorar()
    {
        var ex = Assert.Throws<FonteExternaException>(() => Converter("pagina-propriedade-de-data-renomeada.json"));

        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex.Tipo);
        Assert.Contains("\"Data\"", ex.Message);
    }

    [Fact]
    public void PropriedadeDeDataQueMudouDeTipo_Falha()
    {
        var json = """{"id":"p1","properties":{"Data":{"type":"rich_text","rich_text":[]}}}""";

        var ex = Assert.Throws<FonteExternaException>(() =>
            CriarConversor().Converter(JsonDocument.Parse(json).RootElement, Config));

        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex.Tipo);
    }

    [Fact]
    public void PropriedadeDeConclusaoQueSumiu_Falha()
    {
        var config = Config with { Conclusao = new ConclusaoNotion("Feito", TipoDeConclusaoNotion.Checkbox) };

        var ex = Assert.Throws<FonteExternaException>(() => Converter("pagina-status-concluido.json", config));

        Assert.Contains("\"Feito\"", ex.Message);
    }

    [Fact]
    public void PaginaSemIdOuSemPropriedades_Falha()
    {
        Assert.Throws<FonteExternaException>(() =>
            CriarConversor().Converter(JsonDocument.Parse("""{"properties":{}}""").RootElement, Config));
        Assert.Throws<FonteExternaException>(() =>
            CriarConversor().Converter(JsonDocument.Parse("""{"id":"p1"}""").RootElement, Config));
    }

    // ---------- dados que o app não consegue representar: só aquela página é ignorada ----------

    [Theory]
    [InlineData("9999-12-31T23:59:59.000-03:00")] // início + 1 hora estoura o DateTime
    [InlineData("isto-nao-e-uma-data")]
    [InlineData("2026-13-45")]
    public void DataImpossivelDeConverter_IgnoraAPaginaComAvisoSemDerrubar(string inicio)
    {
        var json = """{"id":"p1","properties":{"Nome":{"type":"title","title":[]},"Data":{"type":"date","date":{"start":"__INICIO__","end":null,"time_zone":null}}}}"""
            .Replace("__INICIO__", inicio);

        var resultado = CriarConversor().Converter(JsonDocument.Parse(json).RootElement, Config);

        Assert.Null(resultado);
        Assert.Single(_log.Avisos, a => a.Contains("p1"));
    }
}
