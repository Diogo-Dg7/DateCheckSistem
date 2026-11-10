using System.IO;
using DateCheck.Desktop.Diagnostico;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Desktop.Tests;

public sealed class ArquivoDeLogTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "DateCheckTests", Guid.NewGuid().ToString("N"), "logs");
    private readonly FakeTimeProvider _relogio = new();

    public ArquivoDeLogTests()
    {
        _relogio.SetLocalTimeZone(TimeZoneInfo.Utc);
        _relogio.SetUtcNow(new DateTimeOffset(2026, 11, 10, 9, 30, 15, 123, TimeSpan.Zero));
    }

    public void Dispose()
    {
        var raiz = Path.GetDirectoryName(_pasta)!;
        if (Directory.Exists(raiz))
            Directory.Delete(raiz, recursive: true);
    }

    private ArquivoDeLogProvider Criar(int retencao = 7) => new(_pasta, _relogio, retencao);

    private string Ler(ArquivoDeLogProvider provider) => File.ReadAllText(provider.CaminhoDeHoje());

    [Fact]
    public void GravaUmaLinhaPorMensagemComDataNivelCategoriaETexto()
    {
        var provider = Criar();
        var log = provider.CreateLogger("DateCheck.Application.Servicos.SincronizacaoExternaService");

        log.LogWarning("Falha na sincronização com {Origem}: {Tipo}.", "Notion", "Indisponivel");

        var linha = Ler(provider).TrimEnd();
        Assert.Equal(
            "2026-11-10 09:30:15.123 [Aviso] DateCheck.Application.Servicos.SincronizacaoExternaService: Falha na sincronização com Notion: Indisponivel.",
            linha);
        Assert.EndsWith("datecheck-20261110.log", provider.CaminhoDeHoje());
    }

    [Theory]
    [InlineData("DateCheck.Algo", LogLevel.Information, true)]
    [InlineData("DateCheck.Algo", LogLevel.Debug, false)]
    [InlineData("Microsoft.Hosting.Lifetime", LogLevel.Information, false)]
    [InlineData("Microsoft.Hosting.Lifetime", LogLevel.Warning, true)]
    [InlineData("System.Net.Http.HttpClient", LogLevel.Error, true)]
    [InlineData("DateCheck.Algo", LogLevel.None, false)]
    public void FiltraPorNivelEPorCategoria(string categoria, LogLevel nivel, bool grava)
    {
        var provider = Criar();
        var log = provider.CreateLogger(categoria);

        Assert.Equal(grava, log.IsEnabled(nivel));
        log.Log(nivel, "mensagem");

        Assert.Equal(grava, File.Exists(provider.CaminhoDeHoje()));
    }

    [Fact]
    public void ExcecaoVaiNaLinhaSeguinte_EQuebrasDeLinhaNaoPartemOLog()
    {
        var provider = Criar();
        var log = provider.CreateLogger("DateCheck.X");

        log.LogError(new InvalidOperationException("falhou\nem duas linhas"), "Erro\r\ninesperado");

        var linhas = Ler(provider).TrimEnd().Split(Environment.NewLine);
        Assert.Equal(2, linhas.Length); // a linha da mensagem + a da exceção, cada uma inteira
        Assert.Contains("Erro | inesperado", linhas[0]);
        Assert.Contains("InvalidOperationException", linhas[1]);
        Assert.Contains("falhou em duas linhas", linhas[1]);
    }

    // ---------- segredos nunca vão para o arquivo ----------

    [Theory]
    [InlineData("token ntn_AbCdEfGh1234567890 recusado")]
    [InlineData("token secret_AbCdEfGh1234567890 recusado")]
    [InlineData("cabeçalho Authorization: Bearer ntn_AbCdEfGh1234567890")]
    [InlineData("cabeçalho Bearer abc.def-ghi_jkl1234567890")]
    [InlineData("NTN_AbCdEfGh1234567890")]
    public void Tokens_SaoMascaradosEmQualquerMensagem(string mensagem)
    {
        var provider = Criar();

        provider.CreateLogger("DateCheck.X").LogWarning("{Texto}", mensagem);

        var gravado = Ler(provider);
        Assert.DoesNotContain("AbCdEfGh1234567890", gravado);
        Assert.DoesNotContain("abc.def-ghi_jkl1234567890", gravado);
        Assert.Contains(ArquivoDeLogProvider.Mascara, gravado);
    }

    [Fact]
    public void Tokens_SaoMascaradosAteNaMensagemDeUmaExcecao()
    {
        var provider = Criar();
        var excecao = new System.Net.Http.HttpRequestException("falha com Bearer ntn_AbCdEfGh1234567890 no cabeçalho");

        provider.CreateLogger("DateCheck.X").LogError(excecao, "Falha de rede");

        Assert.DoesNotContain("ntn_AbCdEfGh1234567890", Ler(provider));
    }

    [Fact]
    public void TextoComum_NaoEMascarado()
    {
        var provider = Criar();

        provider.CreateLogger("DateCheck.X").LogInformation("Sincronização: 3 inseridos; página 0123abcd; chave secreta do Notion");

        var gravado = Ler(provider);
        Assert.DoesNotContain(ArquivoDeLogProvider.Mascara, gravado);
        Assert.Contains("3 inseridos", gravado);
    }

    // ---------- arquivos por dia, retenção e robustez ----------

    [Fact]
    public void UmArquivoPorDia()
    {
        var provider = Criar();
        var log = provider.CreateLogger("DateCheck.X");

        log.LogInformation("dia 10");
        _relogio.Advance(TimeSpan.FromDays(1));
        log.LogInformation("dia 11");

        Assert.Equal(["datecheck-20261110.log", "datecheck-20261111.log"],
            Directory.GetFiles(_pasta).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void ApagaSoOsArquivosAntigosDoLogDoApp()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Path.Combine(_pasta, "datecheck-20261101.log"), "antigo");     // 9 dias: apaga
        File.WriteAllText(Path.Combine(_pasta, "datecheck-20261104.log"), "no limite");   // 6 dias: fica
        File.WriteAllText(Path.Combine(_pasta, "datecheck-20261110.log"), "hoje");        // fica
        File.WriteAllText(Path.Combine(_pasta, "outro-programa.log"), "não é meu");       // nunca mexe
        File.WriteAllText(Path.Combine(_pasta, "datecheck-lixo.log"), "nome inválido");   // nome estranho: ignora

        _ = Criar(retencao: 7);

        Assert.Equal(["datecheck-20261104.log", "datecheck-20261110.log", "datecheck-lixo.log", "outro-programa.log"],
            Directory.GetFiles(_pasta).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void DiaQuePassaDoLimiteDeTamanho_ParaDeCrescer()
    {
        var provider = Criar();
        Directory.CreateDirectory(_pasta);
        using (var fs = File.Create(provider.CaminhoDeHoje()))
            fs.SetLength(ArquivoDeLogProvider.TamanhoMaximoDoDiaEmBytes + 1);
        var tamanho = new FileInfo(provider.CaminhoDeHoje()).Length;

        provider.CreateLogger("DateCheck.X").LogError("não deve ser gravado");

        Assert.Equal(tamanho, new FileInfo(provider.CaminhoDeHoje()).Length);
    }

    [Fact]
    public void FalhaDeDisco_NaoLancaExcecao()
    {
        // O "diretório" do log é, na verdade, um arquivo: criar a pasta e escrever falham.
        var arquivoNoLugarDaPasta = Path.Combine(Path.GetTempPath(), "DateCheckTests", Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(Path.GetDirectoryName(arquivoNoLugarDaPasta)!);
        File.WriteAllText(arquivoNoLugarDaPasta, "sou um arquivo");
        try
        {
            var provider = new ArquivoDeLogProvider(arquivoNoLugarDaPasta, _relogio);

            var excecao = Record.Exception(() => provider.CreateLogger("DateCheck.X").LogError("não pode derrubar o app"));

            Assert.Null(excecao);
        }
        finally
        {
            File.Delete(arquivoNoLugarDaPasta);
        }
    }

    [Fact]
    public async Task EscritaConcorrente_NaoPerdeNemMisturaLinhas()
    {
        var provider = Criar();
        var log = provider.CreateLogger("DateCheck.X");

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
                log.LogInformation("thread {T} linha {I} fim", t, i);
        })));

        var linhas = Ler(provider).TrimEnd().Split(Environment.NewLine);
        Assert.Equal(400, linhas.Length);
        Assert.All(linhas, l => Assert.Matches(@"^\d{4}-\d{2}-\d{2} .* thread \d linha \d+ fim$", l));
    }
}
