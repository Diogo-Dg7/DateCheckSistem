using System.Net;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Interfaces;
using DateCheck.Application.Notion;
using DateCheck.Domain.Entidades;
using DateCheck.Integracoes.Notion;
using DateCheck.Integracoes.Tests.Apoio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Integracoes.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class NotionApiTests : IDisposable
{
    private const string Token = "ntn_TOKEN_SECRETO_DE_TESTE_123456";

    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.CreateCustomTimeZone("Teste-03", TimeSpan.FromHours(-3), "Teste", "Teste");
    private static readonly ConfiguracaoNotion Config = new("db", "ds-0001", "Data");
    private static readonly DateOnly De = new(2026, 8, 12);
    private static readonly DateOnly Ate = new(2027, 11, 10);

    private readonly FakeTempo _relogio = new();
    private readonly HandlerFalso _servidor = new();
    private readonly LogCapturado<NotionApi> _log = new();
    private readonly ServiceProvider _provider;
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "DateCheckTests", Guid.NewGuid().ToString("N"));

    public NotionApiTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_relogio);
        services.AddIntegracoes(_pasta);

        // Mesma cadeia de handlers do app (reintento e limitador); só o "fim da linha" é falso.
        services.AddHttpClient(NotionApi.NomeDoHttpClient).ConfigurePrimaryHttpMessageHandler(() => _servidor);
        services.AddSingleton<ILogger<NotionApi>>(_log);
        services.AddSingleton(new ConversorDePaginaNotion(new LogCapturado<ConversorDePaginaNotion>(), Fuso));

        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    private INotionApi Api => _provider.GetRequiredService<INotionApi>();

    private Task<IReadOnlyList<Agendamento>> Consultar(ConfiguracaoNotion? config = null) =>
        Api.ConsultarAsync(Token, config ?? Config, De, Ate).AvancarAteConcluirAsync(_relogio);

    private HandlerFalso Responder(HttpStatusCode status, string arquivo, TimeSpan? retryAfter = null) =>
        _servidor.Responder(status, Exemplos.Texto(arquivo), retryAfter);

    // ---------- consulta: requisição ----------

    [Fact]
    public async Task Consulta_UsaDataSourceVersaoDaApiTokenEFiltroDeDataComAsDatasPedidas()
    {
        Responder(HttpStatusCode.OK, "consulta-unica.json");

        await Consultar();

        var r = Assert.Single(_servidor.Recebidas);
        Assert.Equal(HttpMethod.Post, r.Metodo);
        Assert.Equal("https://api.notion.com/v1/data_sources/ds-0001/query", r.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("2026-03-11", r.NotionVersion);
        Assert.Equal(NotionApi.VersaoDaApi, r.NotionVersion);
        Assert.Equal($"Bearer {Token}", r.Autorizacao);

        using var corpo = JsonDocument.Parse(r.Corpo!);
        var filtros = corpo.RootElement.GetProperty("filter").GetProperty("and");
        Assert.Equal(2, filtros.GetArrayLength());
        Assert.Equal("Data", filtros[0].GetProperty("property").GetString());
        Assert.Equal("2026-08-12", filtros[0].GetProperty("date").GetProperty("on_or_after").GetString());
        Assert.Equal("Data", filtros[1].GetProperty("property").GetString());
        Assert.Equal("2027-11-10", filtros[1].GetProperty("date").GetProperty("on_or_before").GetString());
        Assert.Equal(100, corpo.RootElement.GetProperty("page_size").GetInt32());
        Assert.False(corpo.RootElement.TryGetProperty("start_cursor", out _)); // primeira página: sem cursor
    }

    [Fact]
    public async Task Consulta_PedeSoTituloDataEConclusao()
    {
        _servidor.Responder(HttpStatusCode.OK, Exemplos.Texto("consulta-pagina-com-status.json"));
        var config = Config with { Conclusao = new ConclusaoNotion("Status", TipoDeConclusaoNotion.Status, "Concluído") };

        var itens = await Consultar(config);

        Assert.Equal(Domain.Enums.StatusAgendamento.Concluido, Assert.Single(itens).Status);

        var query = _servidor.Recebidas.Single().Uri.Query;
        Assert.Contains("filter_properties=title", query);
        Assert.Contains("filter_properties=Data", query);
        Assert.Contains("filter_properties=Status", query);
    }

    [Fact]
    public async Task Consulta_NomeDePropriedadeComEspacosEAcentos_VaiCodificado()
    {
        Responder(HttpStatusCode.OK, "consulta-unica.json");
        var config = Config with { PropriedadeData = "Data de entrega & revisão" };

        // A página de exemplo não tem essa coluna: o conversor reclama, mas a requisição já foi montada.
        await Assert.ThrowsAsync<FonteExternaException>(() => Consultar(config));

        var r = _servidor.Recebidas.Single();
        Assert.Contains("filter_properties=Data%20de%20entrega%20%26%20revis%C3%A3o", r.Uri.Query);
        using var corpo = JsonDocument.Parse(r.Corpo!);
        Assert.Equal("Data de entrega & revisão",
            corpo.RootElement.GetProperty("filter").GetProperty("and")[0].GetProperty("property").GetString());
    }

    // ---------- consulta: paginação e conversão ----------

    [Fact]
    public async Task Consulta_Pagina_SegueOCursorAteAcabar_EFiltraSemDataELixeira()
    {
        Responder(HttpStatusCode.OK, "consulta-pagina-1.json");
        Responder(HttpStatusCode.OK, "consulta-pagina-2.json");

        var itens = await Consultar();

        Assert.Equal(2, _servidor.Recebidas.Count);
        using var corpo2 = JsonDocument.Parse(_servidor.Recebidas[1].Corpo!);
        Assert.Equal("cursor-pagina-2", corpo2.RootElement.GetProperty("start_cursor").GetString());

        // 5 páginas vieram: uma sem data e uma na lixeira não entram.
        Assert.Equal(["Primeira", "Segunda", "Terceira"], itens.Select(i => i.Titulo));
        Assert.All(itens, i => Assert.Equal(Domain.Enums.OrigemAgendamento.Notion, i.Origem));
    }

    [Fact]
    public async Task Consulta_SemResultados_RetornaListaVazia()
    {
        Responder(HttpStatusCode.OK, "consulta-vazia.json");

        Assert.Empty(await Consultar());
    }

    [Fact]
    public async Task Consulta_ResultadoIncompleto_FalhaEmVezDeDevolverListaParcial()
    {
        Responder(HttpStatusCode.OK, "consulta-incompleta.json");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.Incompleta, ex.Tipo);
        // O motivo e o limite aparecem na mensagem, e ela diz que nada foi alterado.
        Assert.Contains("10.000", ex.Message);
        Assert.Contains("limita cada consulta", ex.Message);
        Assert.Contains("Nada foi alterado", ex.Message);
        Assert.Contains("arquive ou mova para outro banco", ex.Message); // diz o que o usuário pode fazer
    }

    [Fact]
    public async Task Consulta_HasMoreSemCursor_FalhaSemLacoInfinito()
    {
        _servidor.Responder(HttpStatusCode.OK, """{"object":"list","results":[],"has_more":true,"next_cursor":null}""");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex.Tipo);
        Assert.Single(_servidor.Recebidas);
    }

    [Fact]
    public async Task Consulta_NaoPassaDoLimiteDePaginas()
    {
        _servidor.Padrao = _ => HandlerFalso.Montar(HttpStatusCode.OK,
            """{"object":"list","results":[],"has_more":true,"next_cursor":"sempre-mais"}""");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.Incompleta, ex.Tipo);
        Assert.Equal(NotionApi.LimiteDePaginas, _servidor.Recebidas.Count); // para exatamente no limite
        Assert.Equal(15_000, NotionApi.LimiteDeItens);
        Assert.Contains("150 páginas", ex.Message);
        Assert.Contains("15.000 itens", ex.Message);
        Assert.Contains("Nada foi alterado", ex.Message);
        Assert.Contains("arquive ou mova para outro banco", ex.Message);
    }

    [Fact]
    public async Task Consulta_JsonMalformadoOuSemResults_EhRespostaInvalida()
    {
        _servidor.Responder(HttpStatusCode.OK, "isto { não é json");
        _servidor.Responder(HttpStatusCode.OK, """{"object":"list"}""");

        var ex1 = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());
        var ex2 = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex1.Tipo);
        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex2.Tipo);
    }

    // ---------- consulta: falhas (nada de lista parcial) ----------

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "erro-401.json", TipoDeFalhaExterna.NaoAutorizado)]
    [InlineData(HttpStatusCode.Forbidden, "erro-403.json", TipoDeFalhaExterna.SemAcesso)]
    [InlineData(HttpStatusCode.NotFound, "erro-404.json", TipoDeFalhaExterna.SemAcesso)]
    public async Task ErrosDeCliente_ViramFalhaTipadaSemReintento(HttpStatusCode status, string arquivo, TipoDeFalhaExterna tipo)
    {
        Responder(status, arquivo);

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(tipo, ex.Tipo);
        Assert.Single(_servidor.Recebidas);
        Assert.Equal(tipo == TipoDeFalhaExterna.NaoAutorizado, ex.ExigeReconexao);
    }

    // ---------- consulta recusada com 400: propriedade configurada que mudou no Notion ----------
    // Achado no teste real (caso 16): renomear "Data" no Notion faz o próprio Notion recusar a consulta (o filtro
    // aponta para uma propriedade que não existe) com 400 validation_error. A mensagem era só "O Notion recusou o
    // pedido (400, validation_error)", sem dizer o que fazer.

    private static string DataSourceCom(params (string Nome, string Tipo)[] propriedades) =>
        "{\"object\":\"data_source\",\"id\":\"ds-0001\",\"properties\":{"
        + string.Join(",", propriedades.Select(p => $"\"{p.Nome}\":{{\"id\":\"x\",\"name\":\"{p.Nome}\",\"type\":\"{p.Tipo}\"}}"))
        + "}}";

    [Fact]
    public async Task Consulta400_ComPropriedadeDeDataQueSumiu_DizQualPropriedadeEOQueFazer()
    {
        Responder(HttpStatusCode.BadRequest, "erro-400-validacao.json");
        _servidor.Responder(HttpStatusCode.OK, DataSourceCom(("Nome", "title"), ("Data2", "date")));

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex.Tipo);
        Assert.Contains("\"Data\"", ex.Message);
        Assert.Contains("não existe mais", ex.Message);
        Assert.Contains("Configurações > Notion", ex.Message);
        Assert.Contains("Nada foi alterado", ex.Message);
        Assert.DoesNotContain("400", ex.Message);
        Assert.DoesNotContain(Token, ex.Message);

        // A consulta recusada e UMA leitura da estrutura do banco para descobrir o motivo; nenhuma tentativa repetida.
        Assert.Equal(2, _servidor.Recebidas.Count);
        Assert.Equal(HttpMethod.Get, _servidor.Recebidas[1].Metodo);
        Assert.Equal("https://api.notion.com/v1/data_sources/ds-0001", _servidor.Recebidas[1].Uri.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task Consulta400_ComPropriedadeDeDataQueMudouDeTipo_DizQueMudouDeTipo()
    {
        Responder(HttpStatusCode.BadRequest, "erro-400-validacao.json");
        _servidor.Responder(HttpStatusCode.OK, DataSourceCom(("Nome", "title"), ("Data", "rich_text")));

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex.Tipo);
        Assert.Contains("\"Data\"", ex.Message);
        Assert.Contains("mudou de tipo", ex.Message);
        Assert.Contains("Configurações > Notion", ex.Message);
    }

    [Fact]
    public async Task Consulta400_ComPropriedadeDeConclusaoQueSumiu_DizQualPropriedade()
    {
        var config = Config with { Conclusao = new ConclusaoNotion("Feito", TipoDeConclusaoNotion.Checkbox) };
        Responder(HttpStatusCode.BadRequest, "erro-400-validacao.json");
        _servidor.Responder(HttpStatusCode.OK, DataSourceCom(("Nome", "title"), ("Data", "date")));

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar(config));

        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex.Tipo);
        Assert.Contains("\"Feito\"", ex.Message);
        Assert.Contains("não existe mais", ex.Message);
        Assert.Contains("Configurações > Notion", ex.Message);
    }

    [Fact]
    public async Task Consulta400_ComEstruturaIntacta_MantemAMensagemGenerica()
    {
        Responder(HttpStatusCode.BadRequest, "erro-400-validacao.json");
        _servidor.Responder(HttpStatusCode.OK, DataSourceCom(("Nome", "title"), ("Data", "date")));

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex.Tipo);
        Assert.Contains("400, validation_error", ex.Message);
    }

    [Fact]
    public async Task Consulta400_SeALeituraDaEstruturaTambemFalha_MantemAMensagemGenerica()
    {
        Responder(HttpStatusCode.BadRequest, "erro-400-validacao.json");
        Responder(HttpStatusCode.NotFound, "erro-404.json");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Contains("400, validation_error", ex.Message);
    }

    [Fact]
    public async Task Consulta401_NaoLeAEstruturaDoBanco()
    {
        // Outros 4xx (401, 403, 404) continuam sem leitura extra: só o 400 de validação investiga.
        Responder(HttpStatusCode.Unauthorized, "erro-401.json");

        await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Single(_servidor.Recebidas);
    }

    [Fact]
    public async Task Consulta403_IndicaOCaminhoAtualDasPermissoes()
    {
        Responder(HttpStatusCode.Forbidden, "erro-403.json");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Contains("app.notion.com/developers/connections", ex.Message);
        Assert.DoesNotContain("my-integrations", ex.Message);
    }

    [Fact]
    public async Task Http429ComRetryAfter_EsperaETentaDeNovo()
    {
        Responder(HttpStatusCode.TooManyRequests, "erro-429.json", TimeSpan.FromSeconds(2));
        Responder(HttpStatusCode.OK, "consulta-unica.json");
        var inicio = _relogio.GetUtcNow();

        var itens = await Consultar();

        Assert.Equal(2, itens.Count);
        Assert.Equal(2, _servidor.Recebidas.Count);
        Assert.True(_relogio.GetUtcNow() - inicio >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Http429Esgotado_ViraLimiteExcedido()
    {
        _servidor.Padrao = _ => HandlerFalso.Montar(HttpStatusCode.TooManyRequests, Exemplos.Texto("erro-429.json"), TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.LimiteExcedido, ex.Tipo);
        Assert.Equal(5, _servidor.Recebidas.Count); // 1 + 4 reintentos
    }

    [Fact]
    public async Task Http503Persistente_ViraIndisponivel()
    {
        _servidor.Padrao = _ => HandlerFalso.Montar(HttpStatusCode.ServiceUnavailable, Exemplos.Texto("erro-503.json"));

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.Indisponivel, ex.Tipo);
    }

    [Fact]
    public async Task FalhaDeRede_ViraIndisponivel()
    {
        _servidor.Padrao = _ => throw new HttpRequestException("sem rede");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.Indisponivel, ex.Tipo);
    }

    [Fact]
    public async Task FalhaNaSegundaPagina_NaoDevolveNada_NemAPrimeira()
    {
        Responder(HttpStatusCode.OK, "consulta-pagina-1.json");
        Responder(HttpStatusCode.Unauthorized, "erro-401.json");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        // A primeira página tinha itens válidos, mas a lista é incompleta: a chamada falha inteira.
        Assert.Equal(TipoDeFalhaExterna.NaoAutorizado, ex.Tipo);
        Assert.Equal(2, _servidor.Recebidas.Count);
    }

    [Fact]
    public async Task Http429EsgotadoNaSegundaPagina_NaoDevolveNada()
    {
        Responder(HttpStatusCode.OK, "consulta-pagina-1.json");
        _servidor.Padrao = _ => HandlerFalso.Montar(HttpStatusCode.TooManyRequests, Exemplos.Texto("erro-429.json"), TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.Equal(TipoDeFalhaExterna.LimiteExcedido, ex.Tipo);
    }

    [Fact]
    public async Task Cancelamento_PropagaOperationCanceledENaoViraFalhaDoNotion()
    {
        Responder(HttpStatusCode.OK, "consulta-unica.json");
        using var cancelamento = new CancellationTokenSource();
        await cancelamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Api.ConsultarAsync(Token, Config, De, Ate, cancelamento.Token));
    }

    [Fact]
    public async Task PropriedadeConfiguradaQueSumiu_FalhaAConsulta()
    {
        _servidor.Responder(HttpStatusCode.OK, Exemplos.Texto("consulta-unica.json"));
        var config = Config with { PropriedadeData = "Prazo" };

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar(config));

        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex.Tipo);
    }

    // ---------- o token nunca vaza ----------

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "erro-401.json")]
    [InlineData(HttpStatusCode.Forbidden, "erro-403.json")]
    [InlineData(HttpStatusCode.NotFound, "erro-404.json")]
    [InlineData(HttpStatusCode.BadRequest, "erro-404.json")]
    [InlineData((HttpStatusCode)418, "erro-404.json")]
    public async Task Token_NuncaApareceEmMensagemExcecaoInternaNemLog(HttpStatusCode status, string arquivo)
    {
        Responder(status, arquivo);

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.DoesNotContain(Token, ex.Message);
        Assert.DoesNotContain(Token, ex.ToString());
        Assert.All(_log.Entradas, e => Assert.DoesNotContain(Token, e.Mensagem));
        Assert.NotEmpty(_log.Entradas); // houve registro, só que sem segredo
    }

    [Fact]
    public async Task Token_NaoApareceNoLogNemQuandoARedeFalha()
    {
        _servidor.Padrao = _ => throw new HttpRequestException($"falha com {Token}");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Consultar());

        Assert.DoesNotContain(Token, ex.Message);
        Assert.All(_log.Entradas, e => Assert.DoesNotContain(Token, e.Mensagem));
    }

    // ---------- estrutura do banco ----------

    [Fact]
    public async Task Estrutura_LeBancoEDataSourceComPropriedadesEOpcoesDeStatus()
    {
        Responder(HttpStatusCode.OK, "banco.json");
        Responder(HttpStatusCode.OK, "data-source.json");

        var estrutura = await Api.ObterEstruturaAsync(Token, "01234567-89ab-cdef-0123-456789abcdef").AvancarAteConcluirAsync(_relogio);

        Assert.Equal("Tarefas", estrutura.Titulo);
        var fonte = Assert.Single(estrutura.FontesDeDados);
        Assert.Equal("ds-0001", fonte.Id);
        Assert.Equal(["Data", "Prazo"], fonte.PropriedadesDeData.Select(p => p.Nome).Order());
        Assert.Equal(["Feito", "Status"], fonte.PropriedadesDeConclusao.Select(p => p.Nome).Order());
        var status = fonte.Propriedades.Single(p => p.Nome == "Status");
        Assert.Equal(["Não iniciado", "Em andamento", "Concluído"], status.Opcoes);

        Assert.Equal("https://api.notion.com/v1/databases/01234567-89ab-cdef-0123-456789abcdef", _servidor.Recebidas[0].Uri.ToString());
        Assert.Equal("https://api.notion.com/v1/data_sources/ds-0001", _servidor.Recebidas[1].Uri.ToString());
        Assert.All(_servidor.Recebidas, r =>
        {
            Assert.Equal(HttpMethod.Get, r.Metodo);
            Assert.Equal("2026-03-11", r.NotionVersion);
        });
    }

    [Fact]
    public async Task Estrutura_BancoComVariosDataSources_TrazTodos()
    {
        Responder(HttpStatusCode.OK, "banco-varios-data-sources.json");
        Responder(HttpStatusCode.OK, "data-source.json");
        Responder(HttpStatusCode.OK, "data-source-sem-data.json");

        var estrutura = await Api.ObterEstruturaAsync(Token, "db").AvancarAteConcluirAsync(_relogio);

        Assert.Equal(["Ativos", "Arquivo"], estrutura.FontesDeDados.Select(f => f.Nome));
        Assert.Empty(estrutura.FontesDeDados[1].PropriedadesDeData);
    }

    [Fact]
    public async Task Estrutura_BancoNaoCompartilhado_EhSemAcessoComMensagemUtil()
    {
        Responder(HttpStatusCode.NotFound, "erro-404.json");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Api.ObterEstruturaAsync(Token, "db"));

        Assert.Equal(TipoDeFalhaExterna.SemAcesso, ex.Tipo);
        Assert.Contains("Conexões", ex.Message);
    }

    [Fact]
    public async Task Estrutura_BancoSemDataSources_EhRespostaInvalida()
    {
        _servidor.Responder(HttpStatusCode.OK, """{"object":"database","id":"db","title":[],"data_sources":[]}""");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Api.ObterEstruturaAsync(Token, "db"));

        Assert.Equal(TipoDeFalhaExterna.RespostaInvalida, ex.Tipo);
    }

    // ---------- identidade ----------

    [Fact]
    public async Task Identidade_MostraNomeDaIntegracaoEDoWorkspace_SemOToken()
    {
        Responder(HttpStatusCode.OK, "usuario-bot.json");

        var identidade = await Api.ObterIdentidadeAsync(Token);

        Assert.Equal("DateCheck (Casa do Diogo)", identidade);
        Assert.DoesNotContain(Token, identidade);
        Assert.Equal("https://api.notion.com/v1/users/me", _servidor.Recebidas.Single().Uri.ToString());
    }

    [Fact]
    public async Task Identidade_TokenInvalido_EhNaoAutorizado()
    {
        Responder(HttpStatusCode.Unauthorized, "erro-401.json");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => Api.ObterIdentidadeAsync("token-errado"));

        Assert.True(ex.ExigeReconexao);
    }

    // ---------- ritmo ----------

    [Fact]
    public async Task VariasRequisicoes_SaemEspacadasPeloLimitador()
    {
        Responder(HttpStatusCode.OK, "consulta-pagina-1.json");
        Responder(HttpStatusCode.OK, "consulta-pagina-2.json");
        var inicio = _relogio.GetUtcNow();

        await Consultar();

        // Duas requisições: a segunda só sai 350 ms depois da primeira.
        Assert.True(_relogio.GetUtcNow() - inicio >= DateCheck.Integracoes.DependencyInjection.IntervaloEntreRequisicoesDoNotion);
    }
}
