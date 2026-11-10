using System.IO;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Servicos;

namespace DateCheck.Desktop.Tests;

public sealed class PreferenciasServiceTests : IDisposable
{
    private sealed class ArmazenamentoEmMemoria : IArmazenamentoDePreferencias
    {
        public Preferencias? Salvo { get; set; }

        public int Gravacoes { get; private set; }

        public Preferencias? Carregar() => Salvo;

        public void Salvar(Preferencias preferencias)
        {
            Salvo = preferencias;
            Gravacoes++;
        }
    }

    private readonly string _pasta = Path.Combine(Path.GetTempPath(), $"DateCheckTestes_{Guid.NewGuid():N}");

    private string Caminho => Path.Combine(_pasta, "DateCheck", "preferencias.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    // ---------- serviço ----------

    [Fact]
    public void SemPreferenciasSalvas_UsaOPadraoDe10Minutos()
    {
        var servico = new PreferenciasService(new ArmazenamentoEmMemoria());

        Assert.Equal(10, servico.Atual.MinutosAntes);
    }

    [Fact]
    public void UsaAsPreferenciasSalvas()
    {
        var servico = new PreferenciasService(new ArmazenamentoEmMemoria { Salvo = new() { MinutosAntes = 25 } });

        Assert.Equal(25, servico.Atual.MinutosAntes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    [InlineData(int.MaxValue)]
    public void PreferenciasSalvasInvalidas_VoltamAoPadrao(int minutos)
    {
        var servico = new PreferenciasService(new ArmazenamentoEmMemoria { Salvo = new() { MinutosAntes = minutos } });

        Assert.Equal(Preferencias.AntecedenciaPadraoEmMinutos, servico.Atual.MinutosAntes);
    }

    [Fact]
    public void Salvar_GravaAtualizaEDisparaOEvento()
    {
        var armazenamento = new ArmazenamentoEmMemoria();
        var servico = new PreferenciasService(armazenamento);
        var disparos = 0;
        servico.Alterado += () => disparos++;

        servico.Salvar(new Preferencias { MinutosAntes = 30 });

        Assert.Equal(30, servico.Atual.MinutosAntes);
        Assert.Equal(30, armazenamento.Salvo!.MinutosAntes);
        Assert.Equal(1, disparos);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1440)]
    public void Salvar_AceitaOsLimites(int minutos)
    {
        var servico = new PreferenciasService(new ArmazenamentoEmMemoria());

        servico.Salvar(new Preferencias { MinutosAntes = minutos });

        Assert.Equal(minutos, servico.Atual.MinutosAntes);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(1441)]
    public void Salvar_ForaDosLimites_LancaENaoGravaNada(int minutos)
    {
        var armazenamento = new ArmazenamentoEmMemoria();
        var servico = new PreferenciasService(armazenamento);
        var disparos = 0;
        servico.Alterado += () => disparos++;

        Assert.Throws<ArgumentException>(() => servico.Salvar(new Preferencias { MinutosAntes = minutos }));

        Assert.Equal(0, armazenamento.Gravacoes);
        Assert.Equal(0, disparos);
        Assert.Equal(10, servico.Atual.MinutosAntes);
    }

    // ---------- arquivo JSON ----------

    [Fact]
    public void Arquivo_SalvaECarregaDeVolta()
    {
        var armazenamento = new ArmazenamentoJsonDePreferencias(Caminho);

        armazenamento.Salvar(new Preferencias { MinutosAntes = 45 });

        Assert.Equal(45, new ArmazenamentoJsonDePreferencias(Caminho).Carregar()!.MinutosAntes);
    }

    [Fact]
    public void Arquivo_CriaAPastaQueNaoExiste()
    {
        Assert.False(Directory.Exists(Path.GetDirectoryName(Caminho)));

        new ArmazenamentoJsonDePreferencias(Caminho).Salvar(new Preferencias());

        Assert.True(File.Exists(Caminho));
    }

    [Fact]
    public void Arquivo_UsaChavesEmCamelCaseELegiveis()
    {
        new ArmazenamentoJsonDePreferencias(Caminho).Salvar(new Preferencias { MinutosAntes = 15 });

        Assert.Contains("\"minutosAntes\": 15", File.ReadAllText(Caminho));
    }

    [Fact]
    public void Arquivo_Inexistente_RetornaNulo()
    {
        Assert.Null(new ArmazenamentoJsonDePreferencias(Caminho).Carregar());
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"minutosAntes\": \"muitos\" }")]
    public void Arquivo_Corrompido_RetornaNuloEOServicoUsaOPadrao(string conteudo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
        File.WriteAllText(Caminho, conteudo);
        var armazenamento = new ArmazenamentoJsonDePreferencias(Caminho);

        Assert.Null(armazenamento.Carregar());
        Assert.Equal(10, new PreferenciasService(armazenamento).Atual.MinutosAntes);
    }

    [Fact]
    public void Arquivo_SemACaveDeMinutos_UsaOValorPadraoDaPropriedade()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
        File.WriteAllText(Caminho, "{ }");

        Assert.Equal(10, new ArmazenamentoJsonDePreferencias(Caminho).Carregar()!.MinutosAntes);
    }

    [Fact]
    public void Arquivo_Sobrescrever_NaoDeixaTemporarioPraTras()
    {
        var armazenamento = new ArmazenamentoJsonDePreferencias(Caminho);
        armazenamento.Salvar(new Preferencias { MinutosAntes = 5 });
        armazenamento.Salvar(new Preferencias { MinutosAntes = 6 });

        Assert.Equal(6, armazenamento.Carregar()!.MinutosAntes);
        Assert.Equal([Caminho], Directory.GetFiles(Path.GetDirectoryName(Caminho)!));
    }

    [Fact]
    public void CaminhoPadrao_FicaEmLocalAppDataDateCheck()
    {
        var caminho = ArmazenamentoJsonDePreferencias.CaminhoPadrao(AmbienteDeExecucao.De(null).PastaDeDados);

        Assert.EndsWith(Path.Combine("DateCheck", "preferencias.json"), caminho);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), caminho);
    }
}
