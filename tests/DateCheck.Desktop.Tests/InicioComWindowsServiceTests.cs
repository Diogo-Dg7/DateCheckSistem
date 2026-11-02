using DateCheck.Desktop.Servicos;
using Microsoft.Win32;

namespace DateCheck.Desktop.Tests;

public class InicioComWindowsServiceTests
{
    private sealed class RegistroEmMemoria : IRegistroDeInicializacao
    {
        public Dictionary<string, string> Valores { get; } = [];

        public string? Ler(string nome) => Valores.GetValueOrDefault(nome);

        public void Gravar(string nome, string valor) => Valores[nome] = valor;

        public void Remover(string nome) => Valores.Remove(nome);
    }

    private readonly RegistroEmMemoria _registro = new();
    private string _caminho = @"C:\Users\Fulano de Tal\DateCheck\DateCheck.Desktop.exe";

    private InicioComWindowsService Criar() => new(_registro, () => _caminho);

    [Fact]
    public void ComecaDesabilitado()
    {
        Assert.False(Criar().Habilitado);
    }

    [Fact]
    public void Habilitar_GravaOCaminhoEntreAspas()
    {
        var servico = Criar();

        servico.Definir(true);

        Assert.True(servico.Habilitado);
        Assert.Equal($"\"{_caminho}\"", _registro.Valores[InicioComWindowsService.NomeDoValor]);
    }

    [Fact]
    public void Desabilitar_RemoveOValor()
    {
        var servico = Criar();
        servico.Definir(true);

        servico.Definir(false);

        Assert.False(servico.Habilitado);
        Assert.Empty(_registro.Valores);
    }

    [Fact]
    public void Desabilitar_QuandoJaDesabilitado_NaoFalha()
    {
        var servico = Criar();

        servico.Definir(false);

        Assert.False(servico.Habilitado);
    }

    [Fact]
    public void Habilitar_DuasVezes_MantemUmSoValor()
    {
        var servico = Criar();

        servico.Definir(true);
        servico.Definir(true);

        Assert.Single(_registro.Valores);
    }

    [Fact]
    public void MudarEstado_DisparaOEventoAlterado()
    {
        var servico = Criar();
        var disparos = 0;
        servico.Alterado += () => disparos++;

        servico.Definir(true);
        servico.Definir(false);

        Assert.Equal(2, disparos);
    }

    [Fact]
    public void CorrigirCaminho_AtualizaQuandoOAppFoiMovido()
    {
        var servico = Criar();
        servico.Definir(true);
        _caminho = @"D:\Apps\DateCheck.Desktop.exe";

        servico.CorrigirCaminhoSeNecessario();

        Assert.Equal("\"D:\\Apps\\DateCheck.Desktop.exe\"", _registro.Valores[InicioComWindowsService.NomeDoValor]);
    }

    [Fact]
    public void CorrigirCaminho_NaoLigaAOpcaoSeOUsuarioNaoLigou()
    {
        var servico = Criar();

        servico.CorrigirCaminhoSeNecessario();

        Assert.False(servico.Habilitado);
        Assert.Empty(_registro.Valores);
    }

    [Fact]
    public void CorrigirCaminho_ComCaminhoIgual_NaoMexe()
    {
        var servico = Criar();
        servico.Definir(true);
        var disparos = 0;
        servico.Alterado += () => disparos++;

        servico.CorrigirCaminhoSeNecessario();

        Assert.Equal(0, disparos);
    }

    // --- Registro real do Windows (chave de teste própria; não toca na entrada do aplicativo) ---

    [Fact]
    public void RegistroReal_GravaLeERemoveUmValorNaChaveRun()
    {
        var nome = $"DateCheckTeste_{Guid.NewGuid():N}";
        var registro = new RegistroDeInicializacaoDoWindows();
        try
        {
            Assert.Null(registro.Ler(nome));

            registro.Gravar(nome, "\"C:\\teste\\app.exe\"");
            Assert.Equal("\"C:\\teste\\app.exe\"", registro.Ler(nome));

            registro.Remover(nome);
            Assert.Null(registro.Ler(nome));
        }
        finally
        {
            using var chave = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            chave?.DeleteValue(nome, throwOnMissingValue: false);
        }
    }

    [Fact]
    public void RegistroReal_RemoverValorInexistente_NaoLanca()
    {
        new RegistroDeInicializacaoDoWindows().Remover($"DateCheckTeste_{Guid.NewGuid():N}");
    }
}
