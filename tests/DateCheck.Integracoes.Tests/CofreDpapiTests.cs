using DateCheck.Integracoes.Segredos;

namespace DateCheck.Integracoes.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class CofreDpapiTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "DateCheckTests", Guid.NewGuid().ToString("N"));
    private readonly CofreDpapi _cofre;

    public CofreDpapiTests()
    {
        _cofre = new CofreDpapi(_pasta);
    }

    public void Dispose()
    {
        if (Directory.Exists(_pasta))
            Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Gravar_ELer_DevolveOMesmoValor()
    {
        _cofre.Gravar("notion-token", "ntn_exemplo_123");

        Assert.Equal("ntn_exemplo_123", _cofre.Ler("notion-token"));
    }

    [Fact]
    public void Ler_SegredoInexistente_RetornaNulo()
    {
        Assert.Null(_cofre.Ler("nao-existe"));
    }

    [Fact]
    public void Ler_PastaInexistente_RetornaNulo()
    {
        Assert.False(Directory.Exists(_pasta));

        Assert.Null(_cofre.Ler("qualquer"));
    }

    [Fact]
    public void Gravar_CriaAPastaESubstituiOValorAnterior()
    {
        _cofre.Gravar("token", "primeiro");
        _cofre.Gravar("token", "segundo");

        Assert.Equal("segundo", _cofre.Ler("token"));
        Assert.Single(Directory.GetFiles(_pasta)); // sem temporários sobrando
    }

    [Fact]
    public void Apagar_RemoveOSegredo_EApagarDeNovoNaoFalha()
    {
        _cofre.Gravar("token", "x");

        _cofre.Apagar("token");
        _cofre.Apagar("token");

        Assert.Null(_cofre.Ler("token"));
        Assert.Empty(Directory.GetFiles(_pasta));
    }

    [Fact]
    public void Apagar_NaoMexeEmOutrosSegredos()
    {
        _cofre.Gravar("a", "1");
        _cofre.Gravar("b", "2");

        _cofre.Apagar("a");

        Assert.Equal("2", _cofre.Ler("b"));
    }

    [Fact]
    public void ArquivoEmDisco_NaoContemOSegredoEmTextoPuro()
    {
        const string segredo = "ntn_SEGREDO_MUITO_RECONHECIVEL_987654321";
        _cofre.Gravar("token", segredo);

        var bytes = File.ReadAllBytes(Directory.GetFiles(_pasta).Single());

        Assert.DoesNotContain(segredo, System.Text.Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain(segredo, System.Text.Encoding.Unicode.GetString(bytes));
    }

    [Fact]
    public void ArquivoCorrompido_RetornaNuloSemLancarENaoApagaOArquivo()
    {
        _cofre.Gravar("token", "valor");
        var arquivo = Directory.GetFiles(_pasta).Single();
        File.WriteAllBytes(arquivo, [1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Null(_cofre.Ler("token"));
        Assert.True(File.Exists(arquivo));
    }

    [Fact]
    public void ArquivoVazio_RetornaNulo()
    {
        _cofre.Gravar("token", "valor");
        File.WriteAllBytes(Directory.GetFiles(_pasta).Single(), []);

        Assert.Null(_cofre.Ler("token"));
    }

    [Fact]
    public void ArquivoProtegidoSemAEntropiaDoApp_NaoEhLido()
    {
        // Outro programa do mesmo usuário protegendo dados "do seu jeito" não é confundido com um segredo nosso.
        Directory.CreateDirectory(_pasta);
        var estranho = System.Security.Cryptography.ProtectedData.Protect(
            [1, 2, 3], null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(_pasta, "token.segredo"), estranho);

        Assert.Null(_cofre.Ler("token"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..\\fora")]
    [InlineData("../fora")]
    [InlineData("C:\\Windows\\x")]
    [InlineData("com espaco")]
    [InlineData("MAIUSCULA")]
    [InlineData("a/b")]
    public void NomeInvalido_EhRecusadoEmTodasAsOperacoes(string nome)
    {
        Assert.Throws<ArgumentException>(() => _cofre.Gravar(nome, "x"));
        Assert.Throws<ArgumentException>(() => _cofre.Ler(nome));
        Assert.Throws<ArgumentException>(() => _cofre.Apagar(nome));
    }

    [Fact]
    public void ValorComAcentosEJsonGrande_VoltaIntacto()
    {
        var json = "{\"client_id\":\"café-ção\",\"x\":\"" + new string('a', 50_000) + "\"}";

        _cofre.Gravar("client-secret", json);

        Assert.Equal(json, _cofre.Ler("client-secret"));
    }
}
