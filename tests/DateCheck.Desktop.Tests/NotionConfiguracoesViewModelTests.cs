using System.IO;
using DateCheck.Application.Dtos;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Notion;
using DateCheck.Application.Servicos;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Tests.Apoio;
using DateCheck.Desktop.ViewModels;

namespace DateCheck.Desktop.Tests;

public class NotionConfiguracoesViewModelTests
{
    private const string Token = "ntn_TOKEN_SECRETO_DE_TESTE";

    private static readonly EstruturaDoBancoNotion Estrutura = new(
        "01234567-89ab-cdef-0123-456789abcdef",
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
            ])
        ]);

    private readonly NotionServiceFalso _notion = new() { Estrutura = Estrutura };
    private readonly SincronizacaoFalsa _sincronizacao = new();
    private readonly FakePreferenciasService _preferencias = new();
    private readonly FakeNavegacao _navegacao = new();
    private readonly FakeDialogService _dialogos = new();

    private NotionConfiguracoesViewModel CriarVm()
    {
        SynchronizationContext.SetSynchronizationContext(null); // eventos de estado rodam direto nos testes
        return new NotionConfiguracoesViewModel(_notion, _sincronizacao, _preferencias, _navegacao, _dialogos);
    }

    private static async Task CarregarAsync(NotionConfiguracoesViewModel vm)
    {
        vm.Token = Token;
        vm.LinkDoBanco = "https://www.notion.so/ws/Tarefas-0123456789abcdef0123456789abcdef";
        await vm.CarregarBancoCommand.ExecuteAsync(null);
    }

    // ---------- carregar o banco ----------

    [Fact]
    public async Task Carregar_PreencheAsListasEEscolheAPrimeiraDataSemConclusao()
    {
        var vm = CriarVm();

        await CarregarAsync(vm);

        Assert.True(vm.MostrarEscolhas);
        Assert.Equal("Tarefas", vm.NomeDoBancoCarregado);
        Assert.Equal(["Data", "Prazo"], vm.PropriedadesDeData.Select(p => p.Nome));
        Assert.Equal("Data", vm.PropriedadeDataEscolhida!.Nome);
        Assert.Equal(["(nenhuma: tudo entra como pendente)", "Feito (caixa de seleção)", "Status (status)"],
            vm.OpcoesDeConclusao.Select(o => o.Texto));
        Assert.True(vm.ConclusaoEscolhida.EhNenhuma);
        Assert.False(vm.MostrarEscolhaDeFonte); // um só data source: não pergunta
        Assert.Equal((Token, "https://www.notion.so/ws/Tarefas-0123456789abcdef0123456789abcdef"), _notion.Inspecoes.Single());
        Assert.False(vm.Ocupado);
    }

    [Fact]
    public async Task Carregar_EscolherStatusMostraAsOpcoesEPreSelecionaAPrimeira()
    {
        var vm = CriarVm();
        await CarregarAsync(vm);

        vm.ConclusaoEscolhida = vm.OpcoesDeConclusao.Single(o => o.Propriedade?.Nome == "Status");

        Assert.True(vm.MostrarValorConcluido);
        Assert.Equal(["Não iniciado", "Concluído"], vm.OpcoesDeStatus);
        Assert.Equal("Não iniciado", vm.ValorConcluidoEscolhido);

        vm.ConclusaoEscolhida = vm.OpcoesDeConclusao.Single(o => o.Propriedade?.Nome == "Feito");
        Assert.False(vm.MostrarValorConcluido);
        Assert.Null(vm.ValorConcluidoEscolhido);
    }

    [Fact]
    public async Task Carregar_ComErro_MostraAMensagemENaoCarregaNada()
    {
        _notion.Falha = new FonteExternaException(TipoDeFalhaExterna.SemAcesso, "O Notion não encontrou o banco.");
        var vm = CriarVm();

        await CarregarAsync(vm);

        Assert.Equal(["O Notion não encontrou o banco."], _dialogos.Erros);
        Assert.False(vm.MostrarEscolhas);
        Assert.Empty(vm.PropriedadesDeData);
        Assert.False(vm.Ocupado); // os botões voltam a funcionar
    }

    [Fact]
    public async Task Carregar_BancoSemPropriedadeDeData_Avisa()
    {
        _notion.Estrutura = new EstruturaDoBancoNotion("db", "Sem datas",
            [new FonteDeDadosNotion("ds-1", "Só nomes", [new PropriedadeNotion("Nome", "title")])]);
        var vm = CriarVm();

        await CarregarAsync(vm);

        Assert.Contains("nenhuma propriedade do tipo data", Assert.Single(_dialogos.Avisos));
        Assert.Empty(vm.PropriedadesDeData);
    }

    [Fact]
    public async Task Carregar_BancoComVariosDataSources_PerguntaEAtualizaAsListasAoTrocar()
    {
        _notion.Estrutura = new EstruturaDoBancoNotion("db", "Projetos",
        [
            new FonteDeDadosNotion("ds-1", "Ativos", [new PropriedadeNotion("Inicio", "date"), new PropriedadeNotion("Feito", "checkbox")]),
            new FonteDeDadosNotion("ds-2", "Arquivo", [new PropriedadeNotion("Quando", "date")])
        ]);
        var vm = CriarVm();

        await CarregarAsync(vm);
        Assert.True(vm.MostrarEscolhaDeFonte);
        Assert.Equal("ds-1", vm.FonteEscolhida!.Id);
        Assert.Equal(["Inicio"], vm.PropriedadesDeData.Select(p => p.Nome));

        vm.FonteEscolhida = vm.FontesDeDados[1];

        Assert.Equal(["Quando"], vm.PropriedadesDeData.Select(p => p.Nome));
        Assert.Single(vm.OpcoesDeConclusao); // só "(nenhuma)": o outro data source não tem checkbox/status
    }

    // ---------- conectar ----------

    [Fact]
    public async Task Conectar_SemCarregarOBanco_AvisaENaoChamaOServico()
    {
        var vm = CriarVm();
        vm.Token = Token;

        await vm.ConectarCommand.ExecuteAsync(null);

        Assert.Single(_dialogos.Avisos);
        Assert.Empty(_notion.Conexoes);
    }

    [Fact]
    public async Task Conectar_EnviaAsEscolhasGuardaOIntervaloLimpaOTokenESincronizaNaHora()
    {
        var vm = CriarVm();
        await CarregarAsync(vm);
        vm.PropriedadeDataEscolhida = vm.PropriedadesDeData.Single(p => p.Nome == "Prazo");
        vm.ConclusaoEscolhida = vm.OpcoesDeConclusao.Single(o => o.Propriedade?.Nome == "Status");
        vm.ValorConcluidoEscolhido = "Concluído";
        vm.IntervaloEmMinutos = 15;

        await vm.ConectarCommand.ExecuteAsync(null);

        var conexao = Assert.Single(_notion.Conexoes);
        Assert.Equal(Token, conexao.Token);
        Assert.Equal("ds-1", conexao.DataSourceId);
        Assert.Equal("Prazo", conexao.PropriedadeData);
        Assert.Equal(new ConclusaoNotion("Status", TipoDeConclusaoNotion.Status, "Concluído"), conexao.Conclusao);
        Assert.Equal(15, _preferencias.Atual.Notion.IntervaloEmMinutos);

        Assert.True(vm.Conectado);
        Assert.Equal(string.Empty, vm.Token); // o token saiu da memória da tela
        Assert.False(vm.MostrarEscolhas);
        Assert.Equal(1, _sincronizacao.Chamadas);
        Assert.True(_sincronizacao.Manuais.Single()); // sincroniza na hora, como "Sincronizar agora"
        Assert.False(vm.Ocupado);
    }

    [Theory]
    [InlineData("Feito", TipoDeConclusaoNotion.Checkbox, null)]
    public async Task Conectar_ComCheckbox_MandaConclusaoDeCheckbox(string propriedade, TipoDeConclusaoNotion tipo, string? valor)
    {
        var vm = CriarVm();
        await CarregarAsync(vm);
        vm.ConclusaoEscolhida = vm.OpcoesDeConclusao.Single(o => o.Propriedade?.Nome == propriedade);

        await vm.ConectarCommand.ExecuteAsync(null);

        Assert.Equal(new ConclusaoNotion(propriedade, tipo, valor), _notion.Conexoes.Single().Conclusao);
    }

    [Fact]
    public async Task Conectar_SemConclusao_MandaNulo()
    {
        var vm = CriarVm();
        await CarregarAsync(vm);

        await vm.ConectarCommand.ExecuteAsync(null);

        Assert.Null(_notion.Conexoes.Single().Conclusao);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(61)]
    public async Task Conectar_ComIntervaloInvalido_AvisaENaoConecta(int minutos)
    {
        var vm = CriarVm();
        await CarregarAsync(vm);
        vm.IntervaloEmMinutos = minutos;

        await vm.ConectarCommand.ExecuteAsync(null);

        Assert.Contains("entre 5 e 60", Assert.Single(_dialogos.Avisos));
        Assert.Empty(_notion.Conexoes);
        Assert.Equal(Token, vm.Token);
    }

    [Fact]
    public async Task Conectar_ComErroDeRegra_MostraEMantemOTokenParaCorrigir()
    {
        var vm = CriarVm();
        await CarregarAsync(vm);
        _notion.Falha = new AplicacaoException("Escolha a propriedade de data.");

        await vm.ConectarCommand.ExecuteAsync(null);

        Assert.Equal(["Escolha a propriedade de data."], _dialogos.Erros);
        Assert.False(vm.Conectado);
        Assert.Equal(Token, vm.Token);
        Assert.Equal(0, _sincronizacao.Chamadas);
        Assert.False(vm.Ocupado);
    }

    [Fact]
    public async Task DepoisDeConectar_NenhumTextoDaTelaNemDialogoContemOToken()
    {
        var vm = CriarVm();
        await CarregarAsync(vm);
        await vm.ConectarCommand.ExecuteAsync(null);
        await vm.TestarConexaoCommand.ExecuteAsync(null);

        var textos = new[]
        {
            vm.ResumoDaConexao, vm.ResultadoDoTeste, vm.LinhaDaSincronizacao, vm.LinhaDeErro, vm.LinkDoBanco, vm.NomeDoBancoCarregado
        }.Concat(_dialogos.Avisos).Concat(_dialogos.Erros).Concat(_dialogos.Confirmacoes.Select(c => c.Mensagem));

        Assert.All(textos, t => Assert.DoesNotContain(Token, t));
        Assert.Equal(string.Empty, vm.Token);
    }

    // ---------- testar, sincronizar, desconectar ----------

    [Fact]
    public async Task Testar_MostraConectadoComo()
    {
        var vm = CriarVm();

        await vm.TestarConexaoCommand.ExecuteAsync(null);

        Assert.Equal("Conectado como DateCheck (Casa). Banco \"Tarefas\" acessível.", vm.ResultadoDoTeste);
        Assert.Equal(1, _notion.Testes);
    }

    [Fact]
    public async Task Testar_ComTokenRecusado_MostraAMensagemSemSegredos()
    {
        _notion.Falha = new FonteExternaException(TipoDeFalhaExterna.NaoAutorizado, "O Notion recusou o token. Reconecte o Notion em Configurações.");
        var vm = CriarVm();

        await vm.TestarConexaoCommand.ExecuteAsync(null);

        Assert.Equal("O Notion recusou o token. Reconecte o Notion em Configurações.", Assert.Single(_dialogos.Erros));
        Assert.Equal(string.Empty, vm.ResultadoDoTeste);
    }

    [Fact]
    public async Task SincronizarAgora_ChamaOServicoComoManual()
    {
        _sincronizacao.Estado = new EstadoDaSincronizacao(SituacaoDaSincronizacao.Ok, new DateTime(2026, 11, 9, 14, 30, 0));
        var vm = CriarVm();

        await vm.SincronizarAgoraCommand.ExecuteAsync(null);

        Assert.True(_sincronizacao.Manuais.Single());
        Assert.Empty(_dialogos.Avisos);
        Assert.Contains("Última sincronização", vm.LinhaDaSincronizacao);
    }

    [Fact]
    public async Task SincronizarAgora_SemConexao_PedeParaConectar()
    {
        var vm = CriarVm();

        await vm.SincronizarAgoraCommand.ExecuteAsync(null);

        Assert.Equal(["Conecte o Notion primeiro."], _dialogos.Avisos);
    }

    [Fact]
    public async Task SincronizarAgora_ComUmaEmAndamento_Avisa()
    {
        _sincronizacao.Estado = new EstadoDaSincronizacao(SituacaoDaSincronizacao.Sincronizando);
        var vm = CriarVm();

        await vm.SincronizarAgoraCommand.ExecuteAsync(null);

        Assert.Contains("andamento", Assert.Single(_dialogos.Avisos));
    }

    [Fact]
    public async Task Desconectar_SemConfirmar_NaoFazNada()
    {
        _notion.EstaConectado = true;
        _dialogos.RespostaConfirmar = false;
        var vm = CriarVm();

        await vm.DesconectarCommand.ExecuteAsync(null);

        Assert.Equal(0, _notion.Desconexoes);
        Assert.True(vm.Conectado);
        Assert.Equal(0, _navegacao.Recarregamentos);
    }

    [Fact]
    public async Task Desconectar_Confirmado_RemoveLimpaARecarregaATela()
    {
        _notion.EstaConectado = true;
        _notion.Removidos = 12;
        var vm = CriarVm();
        Assert.True(vm.Conectado);

        await vm.DesconectarCommand.ExecuteAsync(null);

        var (titulo, mensagem) = Assert.Single(_dialogos.Confirmacoes);
        Assert.Contains("Desconectar", titulo);
        Assert.Contains("apaga o token", mensagem);
        Assert.Contains("compromissos do DateCheck", mensagem); // deixa claro que os locais não são tocados
        Assert.Equal(1, _notion.Desconexoes);
        Assert.False(vm.Conectado);
        Assert.Equal("Não conectado.", vm.ResumoDaConexao);
        Assert.Equal("Desconectado. 12 compromissos do Notion foram removidos.", vm.ResultadoDoTeste);
        Assert.Equal(1, _navegacao.Recarregamentos); // a tela Hoje/Calendário deixa de mostrar os itens removidos
        Assert.False(vm.PodeDesconectar);
    }

    [Fact]
    public async Task Desconectar_UmItem_UsaOSingular()
    {
        _notion.EstaConectado = true;
        _notion.Removidos = 1;
        var vm = CriarVm();

        await vm.DesconectarCommand.ExecuteAsync(null);

        Assert.Equal("Desconectado. 1 compromisso do Notion foi removido.", vm.ResultadoDoTeste);
    }

    [Fact]
    public async Task Desconectar_ComErro_MostraEMantemConectado()
    {
        _notion.EstaConectado = true;
        _notion.Falha = new AplicacaoException("Não foi possível remover.");
        var vm = CriarVm();

        await vm.DesconectarCommand.ExecuteAsync(null);

        Assert.Equal(["Não foi possível remover."], _dialogos.Erros);
        Assert.Equal(0, _navegacao.Recarregamentos);
    }

    // ---------- estado da sincronização na tela ----------

    [Fact]
    public void EstadoOk_MostraDataEContagens_SemErro()
    {
        var estado = new EstadoDaSincronizacao(
            SituacaoDaSincronizacao.Ok, new DateTime(2026, 11, 9, 14, 30, 0), null, new ResultadoSincronizacao(3, 2, 1));

        var linha = NotionConfiguracoesViewModel.FormatarSincronizacao(estado);

        Assert.StartsWith("Última sincronização: ", linha);
        Assert.Contains("3 novo(s), 2 atualizado(s), 1 removido(s)", linha);
        Assert.Equal(string.Empty, NotionConfiguracoesViewModel.FormatarErro(estado));
    }

    [Fact]
    public void EstadoErro_MostraErroEMantemAUltimaSincronizacao()
    {
        var estado = new EstadoDaSincronizacao(SituacaoDaSincronizacao.Erro, new DateTime(2026, 11, 9, 14, 30, 0), "Sem conexão.");

        Assert.StartsWith("Última sincronização: ", NotionConfiguracoesViewModel.FormatarSincronizacao(estado));
        Assert.Equal("Erro: Sem conexão.", NotionConfiguracoesViewModel.FormatarErro(estado));
    }

    [Fact]
    public void EstadoNaoConfigurado_NaoRepeteOResumoDaConexao()
    {
        Assert.Equal(string.Empty, NotionConfiguracoesViewModel.FormatarSincronizacao(EstadoDaSincronizacao.NaoConfigurado));
        Assert.Equal("Não conectado.", CriarVm().ResumoDaConexao);
    }

    [Fact]
    public void EstadoSemSincronizacaoAinda_DizAinda()
    {
        var estado = new EstadoDaSincronizacao(SituacaoDaSincronizacao.Aguardando);

        Assert.Equal("Ainda não sincronizado.", NotionConfiguracoesViewModel.FormatarSincronizacao(estado));
    }

    [Fact]
    public void ReconecteNecessario_MostraMensagemDeReconexaoEMarcaOAviso()
    {
        var vm = CriarVm();

        _sincronizacao.DispararEstado(new EstadoDaSincronizacao(SituacaoDaSincronizacao.ReconecteNecessario, Erro: "token recusado"));

        Assert.True(vm.PrecisaReconectar);
        Assert.StartsWith("Reconecte o Notion", vm.LinhaDeErro);
        Assert.True(vm.TemErro);
    }

    [Fact]
    public void EventoDeEstado_AtualizaATelaEmTempoReal_ESomeAposDescartar()
    {
        var vm = CriarVm();

        _sincronizacao.DispararEstado(new EstadoDaSincronizacao(SituacaoDaSincronizacao.Sincronizando));
        Assert.Equal("Sincronizando…", vm.LinhaDaSincronizacao);

        vm.Dispose();
        _sincronizacao.DispararEstado(new EstadoDaSincronizacao(SituacaoDaSincronizacao.Erro, Erro: "x"));
        Assert.Equal("Sincronizando…", vm.LinhaDaSincronizacao); // não ouve mais
    }

    // ---------- intervalo ----------

    [Fact]
    public void SalvarIntervalo_GravaNasPreferenciasSemApagarOResto()
    {
        _preferencias.Salvar(new Preferencias { MinutosAntes = 25 });
        var vm = CriarVm();
        vm.IntervaloEmMinutos = 30;

        vm.SalvarIntervaloCommand.Execute(null);

        Assert.Equal(30, _preferencias.Atual.Notion.IntervaloEmMinutos);
        Assert.Equal(25, _preferencias.Atual.MinutosAntes);
    }

    [Fact]
    public void SalvarIntervalo_Invalido_AvisaENaoGrava()
    {
        var vm = CriarVm();
        vm.IntervaloEmMinutos = 3;

        vm.SalvarIntervaloCommand.Execute(null);

        Assert.Single(_dialogos.Avisos);
        Assert.Equal(NotionPreferencias.IntervaloPadraoEmMinutos, _preferencias.Atual.Notion.IntervaloEmMinutos);
    }

    [Fact]
    public void AoAbrir_MostraIntervaloSalvoEEstadoDaConexao()
    {
        _preferencias.Salvar(new Preferencias { Notion = new NotionPreferencias { IntervaloEmMinutos = 20 } });
        _notion.EstaConectado = true;
        _notion.ConfiguracaoAtual = new ConfiguracaoNotion("db", "ds", "Data", new ConclusaoNotion("Feito", TipoDeConclusaoNotion.Checkbox));

        var vm = CriarVm();

        Assert.Equal(20, vm.IntervaloEmMinutos);
        Assert.True(vm.Conectado);
        Assert.Contains("\"Data\"", vm.ResumoDaConexao);
        Assert.Contains("\"Feito\"", vm.ResumoDaConexao);
    }
}
