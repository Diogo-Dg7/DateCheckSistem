using System.IO;
using DateCheck.Application.Notion;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Servicos;
using DateCheck.Desktop.Tests.Apoio;
using DateCheck.Desktop.ViewModels;

namespace DateCheck.Desktop.Tests;

public sealed class NotionPreferenciasTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "DateCheckTests", Guid.NewGuid().ToString("N"));

    private string Caminho => Path.Combine(_pasta, "preferencias.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    private PreferenciasService CriarServico() => new(new ArmazenamentoJsonDePreferencias(Caminho));

    // ---------- adaptador de configuração ----------

    [Fact]
    public void SemConfiguracao_ObterRetornaNulo()
    {
        var adaptador = new ConfiguracaoNotionDasPreferencias(CriarServico());

        Assert.Null(adaptador.Obter());
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("Feito", "checkbox", null)]
    [InlineData("Status", "status", "Concluído")]
    public void SalvarEObter_FazOIdaEVoltaPeloArquivo(string? propriedade, string? tipo, string? valor)
    {
        ConclusaoNotion? conclusao = propriedade is null
            ? null
            : new ConclusaoNotion(propriedade, tipo == "checkbox" ? TipoDeConclusaoNotion.Checkbox : TipoDeConclusaoNotion.Status, valor);
        var original = new ConfiguracaoNotion("db-1", "ds-1", "Data", conclusao);

        new ConfiguracaoNotionDasPreferencias(CriarServico()).Salvar(original);
        var relida = new ConfiguracaoNotionDasPreferencias(CriarServico()).Obter(); // novo processo: lê do disco

        Assert.Equal(original, relida);
    }

    [Fact]
    public void Salvar_PreservaAntecedenciaEIntervalo()
    {
        var servico = CriarServico();
        servico.Salvar(servico.Atual with
        {
            MinutosAntes = 25,
            Notion = new NotionPreferencias { IntervaloEmMinutos = 30 }
        });

        new ConfiguracaoNotionDasPreferencias(servico).Salvar(new ConfiguracaoNotion("db", "ds", "Data"));

        Assert.Equal(25, servico.Atual.MinutosAntes);
        Assert.Equal(30, servico.Atual.Notion.IntervaloEmMinutos);
    }

    [Fact]
    public void Limpar_ApagaABancoEPropriedadesMasMantemOIntervaloEAAntecedencia()
    {
        var servico = CriarServico();
        var adaptador = new ConfiguracaoNotionDasPreferencias(servico);
        servico.Salvar(servico.Atual with { MinutosAntes = 25, Notion = new NotionPreferencias { IntervaloEmMinutos = 45 } });
        adaptador.Salvar(new ConfiguracaoNotion("db", "ds", "Data", new ConclusaoNotion("Status", TipoDeConclusaoNotion.Status, "Feito")));

        adaptador.Limpar();

        Assert.Null(adaptador.Obter());
        Assert.Equal(45, servico.Atual.Notion.IntervaloEmMinutos);
        Assert.Equal(25, servico.Atual.MinutosAntes);
        Assert.Null(servico.Atual.Notion.ConclusaoPropriedade);
    }

    [Fact]
    public void ConfiguracaoIncompleta_NaoContaComoConfigurada()
    {
        var servico = CriarServico();
        servico.Salvar(servico.Atual with { Notion = new NotionPreferencias { DatabaseId = "db", DataSourceId = "ds" } }); // sem data

        Assert.Null(new ConfiguracaoNotionDasPreferencias(servico).Obter());
    }

    [Fact]
    public void ArquivoDePreferencias_NaoTemNenhumCampoDeSegredo()
    {
        var servico = CriarServico();
        new ConfiguracaoNotionDasPreferencias(servico).Salvar(new ConfiguracaoNotion("db", "ds", "Data"));

        var json = File.ReadAllText(Caminho);

        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"databaseId\": \"db\"", json);
    }

    // ---------- compatibilidade e limites ----------

    [Fact]
    public void ArquivoDaV010_SemASecaoNotion_CarregaComPadroes()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Caminho, """{ "minutosAntes": 20 }""");

        var servico = CriarServico();

        Assert.Equal(20, servico.Atual.MinutosAntes);
        Assert.Equal(NotionPreferencias.IntervaloPadraoEmMinutos, servico.Atual.Notion.IntervaloEmMinutos);
        Assert.Null(new ConfiguracaoNotionDasPreferencias(servico).Obter());
    }

    [Fact]
    public void SecaoNotionNula_VoltaAoPadraoSemQuebrar()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Caminho, """{ "minutosAntes": 20, "notion": null }""");

        var servico = CriarServico();

        Assert.NotNull(servico.Atual.Notion);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(60)]
    public void Intervalo_AceitaDe5a60(int minutos)
    {
        var servico = CriarServico();

        servico.Salvar(servico.Atual with { Notion = servico.Atual.Notion with { IntervaloEmMinutos = minutos } });

        Assert.Equal(minutos, servico.Atual.Notion.IntervaloEmMinutos);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(61)]
    [InlineData(-5)]
    public void Intervalo_ForaDeLimites_LancaENaoGrava(int minutos)
    {
        var servico = CriarServico();

        Assert.Throws<ArgumentException>(() =>
            servico.Salvar(servico.Atual with { Notion = servico.Atual.Notion with { IntervaloEmMinutos = minutos } }));

        Assert.Equal(NotionPreferencias.IntervaloPadraoEmMinutos, servico.Atual.Notion.IntervaloEmMinutos);
        Assert.False(File.Exists(Caminho));
    }

    [Fact]
    public void ArquivoComIntervaloAbsurdo_VoltaAoPadrao()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Caminho, """{ "minutosAntes": 20, "notion": { "intervaloEmMinutos": 1 } }""");

        Assert.Equal(Preferencias.Padrao, CriarServico().Atual);
    }

    // ---------- a tela de configurações não apaga a seção do Notion ----------

    [Fact]
    public void SalvarAntecedenciaNasConfiguracoes_NaoApagaAConfiguracaoDoNotion()
    {
        var servico = CriarServico();
        new ConfiguracaoNotionDasPreferencias(servico).Salvar(new ConfiguracaoNotion("db", "ds", "Data"));
        servico.Salvar(servico.Atual with { Notion = servico.Atual.Notion with { IntervaloEmMinutos = 30 } });
        var vm = new ConfiguracoesViewModel(servico, new InicioFalso(), new FakeDialogService()) { MinutosAntes = 15 };

        vm.SalvarCommand.Execute(null);

        Assert.Equal(15, servico.Atual.MinutosAntes);
        Assert.Equal(30, servico.Atual.Notion.IntervaloEmMinutos);
        Assert.NotNull(new ConfiguracaoNotionDasPreferencias(servico).Obter());
    }

    private sealed class InicioFalso : IInicioComWindowsService
    {
        public bool Habilitado { get; private set; }

        public event Action? Alterado;

        public void Definir(bool habilitado)
        {
            Habilitado = habilitado;
            Alterado?.Invoke();
        }
    }
}
