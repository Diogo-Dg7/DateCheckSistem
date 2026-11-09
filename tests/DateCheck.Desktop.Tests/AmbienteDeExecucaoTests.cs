using System.IO;
using System.Text.Json;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Servicos;

namespace DateCheck.Desktop.Tests;

/// <summary>Isolamento da build de desenvolvimento em relação ao app instalado.</summary>
public class AmbienteDeExecucaoTests
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData(" DEVELOPMENT ")]
    public void DotnetEnvironmentDevelopment_EhDesenvolvimento(string valor)
    {
        Assert.True(AmbienteDeExecucao.De(valor).EhDesenvolvimento);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void SemVariavelOuOutroValor_EhProducao(string? valor)
    {
        Assert.False(AmbienteDeExecucao.De(valor).EhDesenvolvimento);
    }

    [Fact]
    public void Producao_MantemOsNomesDaV010()
    {
        var prod = AmbienteDeExecucao.De(null);

        Assert.Equal("DateCheckSistem", prod.NomeDaInstancia);
        Assert.Equal(Path.Combine(LocalAppData, "DateCheck"), prod.PastaDeDados);
        Assert.Equal("DateCheck", prod.TituloDoApp);
    }

    [Fact]
    public void Desenvolvimento_UsaOutroMutexOutraPastaEOutroTitulo()
    {
        var prod = AmbienteDeExecucao.De(null);
        var dev = AmbienteDeExecucao.De("Development");

        Assert.NotEqual(prod.NomeDaInstancia, dev.NomeDaInstancia);
        Assert.Equal(Path.Combine(LocalAppData, "DateCheck-Dev"), dev.PastaDeDados);
        Assert.NotEqual(prod.PastaDeDados, dev.PastaDeDados);
        Assert.Equal("DateCheck (Dev)", dev.TituloDoApp);
    }

    [Fact]
    public void InstanciasDeProducaoEDesenvolvimento_PodemRodarAoMesmoTempo()
    {
        // O sufixo único evita colidir com um app de verdade aberto na máquina; o que se testa é que as bases diferem.
        var sufixo = Guid.NewGuid().ToString("N");
        using var producao = new InstanciaUnica(AmbienteDeExecucao.De(null).NomeDaInstancia + sufixo);
        using var desenvolvimento = new InstanciaUnica(AmbienteDeExecucao.De("Development").NomeDaInstancia + sufixo);

        Assert.True(producao.TentarAssumir());
        Assert.True(desenvolvimento.TentarAssumir());
    }

    [Fact]
    public void PreferenciasFicamNaPastaDoAmbiente()
    {
        var caminho = ArmazenamentoJsonDePreferencias.CaminhoPadrao(AmbienteDeExecucao.De("Development").PastaDeDados);

        Assert.Equal(Path.Combine(LocalAppData, "DateCheck-Dev", "preferencias.json"), caminho);
    }

    // ---------- chave Run ----------

    [Fact]
    public void Desenvolvimento_NaoUsaORegistroDoWindows()
    {
        var registro = AmbienteDeExecucao.De("Development").CriarRegistroDeInicializacao();

        Assert.IsType<RegistroDeInicializacaoEmMemoria>(registro);
    }

    [Fact]
    public void Producao_UsaORegistroDoWindows()
    {
        var registro = AmbienteDeExecucao.De(null).CriarRegistroDeInicializacao();

        Assert.IsType<RegistroDeInicializacaoDoWindows>(registro);
    }

    [Fact]
    public void RegistroEmMemoria_FuncionaComoOReal_SemPersistirNada()
    {
        var registro = new RegistroDeInicializacaoEmMemoria();
        var servico = new InicioComWindowsService(registro, () => @"C:\Dev\bin\Debug\DateCheck.Desktop.exe");

        Assert.False(servico.Habilitado);
        servico.Definir(true);
        Assert.True(servico.Habilitado);
        Assert.Equal(@"""C:\Dev\bin\Debug\DateCheck.Desktop.exe""", registro.Ler(InicioComWindowsService.NomeDoValor));
        servico.Definir(false);
        Assert.False(servico.Habilitado);

        // Uma nova instância (como num novo processo) não herda nada.
        servico.Definir(true);
        Assert.False(new InicioComWindowsService(new RegistroDeInicializacaoEmMemoria(), () => "x").Habilitado);
    }

    // ---------- banco ----------

    [Theory]
    [InlineData(@"Server=.\SQLEXPRESS;Database=DateCheckDb;Trusted_Connection=True;")]
    [InlineData(@"Server=.\SQLEXPRESS;Initial Catalog=DateCheckDb;Trusted_Connection=True;")]
    [InlineData(@"Server=.\SQLEXPRESS;database=DATECHECKDB;Trusted_Connection=True;")]
    [InlineData(@"Server=.\SQLEXPRESS;Trusted_Connection=True;")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("isto não é uma connection string")]
    public void Desenvolvimento_RecusaOBancoRealOuSemBanco(string? conexao)
    {
        var dev = AmbienteDeExecucao.De("Development");

        Assert.Throws<InvalidOperationException>(() => dev.GarantirBancoIsolado(conexao));
    }

    [Fact]
    public void Desenvolvimento_AceitaOutroBanco()
    {
        AmbienteDeExecucao.De("Development")
            .GarantirBancoIsolado(@"Server=.\SQLEXPRESS;Database=DateCheckDb_Dev;Trusted_Connection=True;");
    }

    [Fact]
    public void Producao_NaoImpoeRestricaoAoBanco()
    {
        var prod = AmbienteDeExecucao.De(null);

        prod.GarantirBancoIsolado(@"Server=.\SQLEXPRESS;Database=DateCheckDb;Trusted_Connection=True;");
        prod.GarantirBancoIsolado(@"Server=.\SQLEXPRESS;Database=DateCheckDb_Capturas;Trusted_Connection=True;");
    }

    [Fact]
    public void AppsettingsDevelopment_ApontaParaDateCheckDbDevEPassaATrava()
    {
        var arquivo = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        Assert.True(File.Exists(arquivo), "appsettings.Development.json precisa ser copiado para a saída do Desktop.");

        using var json = JsonDocument.Parse(File.ReadAllText(arquivo));
        var conexao = json.RootElement.GetProperty("ConnectionStrings").GetProperty("DateCheckDb").GetString();

        Assert.Equal("DateCheckDb_Dev", AmbienteDeExecucao.NomeDoBanco(conexao));
        AmbienteDeExecucao.De("Development").GarantirBancoIsolado(conexao);
    }

    [Fact]
    public void AppsettingsDeProducao_ContinuaNoBancoReal()
    {
        var arquivo = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var json = JsonDocument.Parse(File.ReadAllText(arquivo));
        var conexao = json.RootElement.GetProperty("ConnectionStrings").GetProperty("DateCheckDb").GetString();

        Assert.Equal(AmbienteDeExecucao.NomeDoBancoReal, AmbienteDeExecucao.NomeDoBanco(conexao));
    }
}
