using DateCheck.Application.Excecoes;
using DateCheck.Application.Notion;
using DateCheck.Application.Servicos;
using DateCheck.Application.Tests.Fakes;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Application.Tests;

public class NotionServicosTests
{
    private const string Token = "ntn_token_de_teste";

    private static readonly EstruturaDoBancoNotion Estrutura = new(
        "banco-original",
        "Tarefas",
        [
            new FonteDeDadosNotion("ds-1", "Tarefas",
            [
                new PropriedadeNotion("Nome", "title"),
                new PropriedadeNotion("Data", "date"),
                new PropriedadeNotion("Prazo", "date"),
                new PropriedadeNotion("Feito", "checkbox"),
                new PropriedadeNotion("Status", "status", ["Não iniciado", "Concluído"]),
                new PropriedadeNotion("Categoria", "select")
            ]),
            new FonteDeDadosNotion("ds-2", "Arquivo", [new PropriedadeNotion("Quando", "date")])
        ]);

    private readonly CofreEmMemoria _cofre = new();
    private readonly ConfiguracaoNotionEmMemoria _configuracao = new();
    private readonly NotionApiFalsa _api = new() { Estrutura = Estrutura };
    private readonly FakeAgendamentoRepository _repositorio = new();
    private readonly FakeTimeProvider _relogio = new();
    private readonly NotionFonte _fonte;
    private readonly SincronizacaoExternaService _sincronizacao;
    private readonly NotionService _servico;

    public NotionServicosTests()
    {
        _relogio.SetLocalTimeZone(TimeZoneInfo.Utc);
        _relogio.SetUtcNow(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        _fonte = new NotionFonte(_cofre, _configuracao, _api);
        _sincronizacao = new SincronizacaoExternaService(_fonte, _repositorio, _relogio, NullLogger<SincronizacaoExternaService>.Instance);
        _servico = new NotionService(_cofre, _configuracao, _api, _repositorio, _sincronizacao);
    }

    private void Conectar(ConclusaoNotion? conclusao = null)
    {
        _cofre.Gravar(SegredosDoNotion.Token, Token);
        _configuracao.Salvar(new ConfiguracaoNotion("db", "ds-1", "Data", conclusao));
    }

    private Agendamento SemearNotion(string id, DateTime inicio) =>
        _repositorio.Semear(Agendamento.Importar(OrigemAgendamento.Notion, id, null, id, null, inicio, inicio.AddHours(1), false, StatusAgendamento.Pendente));

    // ---------- NotionFonte ----------

    [Fact]
    public async Task Fonte_PedeAoNotionComUmDiaDeFolgaEmCadaPonta()
    {
        Conectar();
        var inicio = new DateTime(2026, 8, 12);
        var fim = new DateTime(2027, 11, 11); // exclusivo

        await _fonte.ListarAsync(inicio, fim);

        var consulta = Assert.Single(_api.Consultas);
        Assert.Equal(new DateOnly(2026, 8, 11), consulta.De);  // 1 dia antes do início
        Assert.Equal(new DateOnly(2027, 11, 11), consulta.Ate); // o dia do fim (exclusivo): 1 dia depois do último dia da janela
        Assert.Equal(Token, _api.TokensRecebidos.Single());
        Assert.Equal("Data", consulta.Configuracao.PropriedadeData);
    }

    [Fact]
    public async Task Fonte_SemToken_EhNaoAutorizado_SemChamarAApi()
    {
        _configuracao.Salvar(new ConfiguracaoNotion("db", "ds-1", "Data"));

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => _fonte.ListarAsync(DateTime.Today, DateTime.Today.AddDays(1)));

        Assert.True(ex.ExigeReconexao);
        Assert.Empty(_api.Consultas);
    }

    [Fact]
    public async Task Fonte_SemConfiguracao_Falha_SemChamarAApi()
    {
        _cofre.Gravar(SegredosDoNotion.Token, Token);

        await Assert.ThrowsAsync<FonteExternaException>(() => _fonte.ListarAsync(DateTime.Today, DateTime.Today.AddDays(1)));

        Assert.Empty(_api.Consultas);
    }

    [Fact]
    public void Fonte_EstaConfiguradaSoComTokenEConfiguracao()
    {
        Assert.False(_fonte.EstaConfigurada);

        _cofre.Gravar(SegredosDoNotion.Token, Token);
        Assert.False(_fonte.EstaConfigurada);

        _configuracao.Salvar(new ConfiguracaoNotion("db", "ds-1", "Data"));
        Assert.True(_fonte.EstaConfigurada);

        _cofre.Apagar(SegredosDoNotion.Token);
        Assert.False(_fonte.EstaConfigurada);
    }

    [Fact]
    public async Task Fonte_FalhaDaApiPropagaSemMascarar()
    {
        Conectar();
        _api.Falha = new FonteExternaException(TipoDeFalhaExterna.Indisponivel, "sem rede");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => _fonte.ListarAsync(DateTime.Today, DateTime.Today.AddDays(1)));

        Assert.Equal(TipoDeFalhaExterna.Indisponivel, ex.Tipo);
    }

    // ---------- InspecionarAsync ----------

    [Fact]
    public async Task Inspecionar_ExtraiOIdDoLinkEDevolveAEstrutura_SemGravarNada()
    {
        var estrutura = await _servico.InspecionarAsync(
            $"  {Token}  ", "https://www.notion.so/ws/Tarefas-0123456789abcdef0123456789abcdef?v=ffffffffffffffffffffffffffffffff");

        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", estrutura.DatabaseId);
        Assert.Equal(Token, _api.TokensRecebidos.Single()); // sem espaços
        Assert.Null(_cofre.Ler(SegredosDoNotion.Token)); // inspecionar não grava token
        Assert.Null(_configuracao.Obter());
    }

    [Theory]
    [InlineData("", "https://www.notion.so/Tarefas-0123456789abcdef0123456789abcdef")]
    [InlineData("   ", "https://www.notion.so/Tarefas-0123456789abcdef0123456789abcdef")]
    [InlineData(Token, "")]
    [InlineData(Token, "isto não é um link")]
    [InlineData(Token, "https://exemplo.com/Tarefas-0123456789abcdef0123456789abcdef")]
    public async Task Inspecionar_TokenOuLinkInvalidos_SaoRecusadosSemChamarAApi(string token, string link)
    {
        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.InspecionarAsync(token, link));

        Assert.Equal(0, _api.ChamadasDeEstrutura);
    }

    // ---------- ConectarAsync ----------

    [Fact]
    public async Task Conectar_GravaTokenNoCofreEConfiguracaoSeparadamenteEReiniciaASincronizacao()
    {
        await _servico.ConectarAsync($" {Token} ", Estrutura, "ds-1", "Data",
            new ConclusaoNotion("Status", TipoDeConclusaoNotion.Status, "Concluído"));

        Assert.Equal(Token, _cofre.Ler(SegredosDoNotion.Token));
        var config = _configuracao.Obter()!;
        Assert.Equal("banco-original", config.DatabaseId);
        Assert.Equal("ds-1", config.DataSourceId);
        Assert.Equal("Data", config.PropriedadeData);
        Assert.Equal("Concluído", config.Conclusao!.ValorConcluido);
        Assert.DoesNotContain(Token, config.ToString()); // o token não está na configuração não secreta
        Assert.True(_servico.EstaConectado);
        Assert.Equal(SituacaoDaSincronizacao.Aguardando, _sincronizacao.Estado.Situacao);
    }

    [Fact]
    public async Task Conectar_SemConclusao_EValido()
    {
        await _servico.ConectarAsync(Token, Estrutura, "ds-1", "Prazo", conclusao: null);

        Assert.Null(_configuracao.Obter()!.Conclusao);
    }

    [Fact]
    public async Task Conectar_Checkbox_EValido()
    {
        await _servico.ConectarAsync(Token, Estrutura, "ds-1", "Data", new ConclusaoNotion("Feito", TipoDeConclusaoNotion.Checkbox));

        Assert.Equal(TipoDeConclusaoNotion.Checkbox, _configuracao.Obter()!.Conclusao!.Tipo);
    }

    public static TheoryData<string, string, string, string?> EscolhasInvalidas => new()
    {
        { Token, "ds-inexistente", "Data", null },
        { Token, "ds-1", "Nome", null },          // título não é data
        { Token, "ds-1", "Categoria", null },     // select não é data
        { Token, "ds-1", "Quando", null },        // existe, mas em outro data source
        { Token, "ds-1", "NaoExiste", null },
        { "", "ds-1", "Data", null },
        { Token, "ds-1", "Data", "Feito:status:Concluído" },  // Feito é checkbox, não status
        { Token, "ds-1", "Data", "Status:status:Inexistente" }, // opção que não existe
        { Token, "ds-1", "Data", "Status:status:" },           // status sem valor concluído
        { Token, "ds-1", "Data", "Status:checkbox:" },         // status não é checkbox
        { Token, "ds-1", "Data", "Sumiu:checkbox:" },          // propriedade inexistente
    };

    [Theory]
    [MemberData(nameof(EscolhasInvalidas))]
    public async Task Conectar_EscolhaInvalida_EhRecusadaSemGravarNada(string token, string dataSource, string propriedadeData, string? conclusao)
    {
        ConclusaoNotion? c = null;
        if (conclusao is not null)
        {
            var partes = conclusao.Split(':');
            c = new ConclusaoNotion(partes[0], partes[1] == "status" ? TipoDeConclusaoNotion.Status : TipoDeConclusaoNotion.Checkbox,
                string.IsNullOrEmpty(partes[2]) ? null : partes[2]);
        }

        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.ConectarAsync(token, Estrutura, dataSource, propriedadeData, c));

        Assert.Null(_cofre.Ler(SegredosDoNotion.Token));
        Assert.Null(_configuracao.Obter());
        Assert.False(_servico.EstaConectado);
    }

    [Fact]
    public async Task Conectar_Reconectar_SubstituiOTokenAntigo()
    {
        await _servico.ConectarAsync("token-velho", Estrutura, "ds-1", "Data", null);

        await _servico.ConectarAsync("token-novo", Estrutura, "ds-1", "Data", null);

        Assert.Equal("token-novo", _cofre.Ler(SegredosDoNotion.Token));
    }

    // ---------- TestarConexaoAsync ----------

    [Fact]
    public async Task Testar_MostraConectadoComoSemOToken()
    {
        Conectar();

        var mensagem = await _servico.TestarConexaoAsync();

        Assert.Equal("Conectado como DateCheck (Casa). Banco \"Tarefas\" acessível.", mensagem);
        Assert.DoesNotContain(Token, mensagem);
    }

    [Fact]
    public async Task Testar_SemConexao_Falha()
    {
        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.TestarConexaoAsync());
    }

    [Fact]
    public async Task Testar_PropriedadeDeDataQueSumiuDoBanco_AvisaParaEscolherDeNovo()
    {
        _cofre.Gravar(SegredosDoNotion.Token, Token);
        _configuracao.Salvar(new ConfiguracaoNotion("db", "ds-1", "Coluna que sumiu"));

        var ex = await Assert.ThrowsAsync<AplicacaoException>(() => _servico.TestarConexaoAsync());

        Assert.Contains("Coluna que sumiu", ex.Message);
    }

    [Fact]
    public async Task Testar_DataSourceQueSumiu_Avisa()
    {
        _cofre.Gravar(SegredosDoNotion.Token, Token);
        _configuracao.Salvar(new ConfiguracaoNotion("db", "ds-que-sumiu", "Data"));

        await Assert.ThrowsAsync<AplicacaoException>(() => _servico.TestarConexaoAsync());
    }

    [Fact]
    public async Task Testar_TokenRecusado_PropagaFalhaDoNotion()
    {
        Conectar();
        _api.Falha = new FonteExternaException(TipoDeFalhaExterna.NaoAutorizado, "recusado");

        var ex = await Assert.ThrowsAsync<FonteExternaException>(() => _servico.TestarConexaoAsync());

        Assert.True(ex.ExigeReconexao);
    }

    // ---------- DesconectarAsync ----------

    [Fact]
    public async Task Desconectar_ApagaTokenEConfiguracaoERemoveSoOsItensDoNotion()
    {
        Conectar();
        var local = _repositorio.Semear(new Agendamento("Meu", new DateTime(2026, 11, 12, 9, 0, 0), new DateTime(2026, 11, 12, 10, 0, 0)));
        SemearNotion("n1", new DateTime(2026, 11, 12, 9, 0, 0));
        SemearNotion("n2", new DateTime(2026, 11, 13, 9, 0, 0));
        SemearNotion("n-antigo", new DateTime(2025, 1, 1, 9, 0, 0));    // bem fora da janela de sincronização
        SemearNotion("n-distante", new DateTime(2031, 1, 1, 9, 0, 0));

        var removidos = await _servico.DesconectarAsync();

        Assert.Equal(4, removidos);
        Assert.Same(local, Assert.Single(_repositorio.Itens));
        Assert.Null(_cofre.Ler(SegredosDoNotion.Token));
        Assert.Null(_configuracao.Obter());
        Assert.False(_servico.EstaConectado);
        Assert.Equal(SituacaoDaSincronizacao.NaoConfigurado, _sincronizacao.Estado.Situacao);
    }

    [Fact]
    public async Task Desconectar_SemNadaImportado_Funciona()
    {
        Conectar();

        Assert.Equal(0, await _servico.DesconectarAsync());
        Assert.False(_servico.EstaConectado);
    }

    [Fact]
    public async Task Desconectar_EsperaUmaSincronizacaoEmAndamentoTerminar()
    {
        Conectar();
        var fonte = new FonteFalsa { Liberacao = new TaskCompletionSource() };
        var sincronizacao = new SincronizacaoExternaService(fonte, _repositorio, _relogio, NullLogger<SincronizacaoExternaService>.Instance);
        var servico = new NotionService(_cofre, _configuracao, _api, _repositorio, sincronizacao);

        var emAndamento = sincronizacao.SincronizarAsync();
        var desconexao = servico.DesconectarAsync();
        await Task.Delay(50);

        Assert.False(desconexao.IsCompleted);
        Assert.Equal(Token, _cofre.Ler(SegredosDoNotion.Token)); // ainda conectado até a sincronização acabar

        fonte.Liberacao.SetResult();
        await emAndamento;
        await desconexao;
        Assert.Null(_cofre.Ler(SegredosDoNotion.Token));
    }
}
