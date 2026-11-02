using System.IO;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Navegacao;
using DateCheck.Desktop.Servicos;
using DateCheck.Desktop.Tests.Apoio;
using DateCheck.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DateCheck.Desktop.Tests;

public class ConfiguracoesViewModelTests
{
    private sealed class RegistroEmMemoria : IRegistroDeInicializacao
    {
        public Dictionary<string, string> Valores { get; } = [];

        public string? Ler(string nome) => Valores.GetValueOrDefault(nome);

        public void Gravar(string nome, string valor) => Valores[nome] = valor;

        public void Remover(string nome) => Valores.Remove(nome);
    }

    private readonly FakePreferenciasService _preferencias = new();
    private readonly RegistroEmMemoria _registro = new();
    private readonly FakeDialogService _dialogos = new();
    private readonly InicioComWindowsService _inicio;
    private readonly List<bool> _fechamentos = [];

    public ConfiguracoesViewModelTests()
    {
        _inicio = new InicioComWindowsService(_registro, () => @"C:\DateCheck\DateCheck.Desktop.exe");
    }

    private ConfiguracoesViewModel Criar()
    {
        var vm = new ConfiguracoesViewModel(_preferencias, _inicio, _dialogos);
        vm.FechamentoSolicitado += _fechamentos.Add;
        return vm;
    }

    [Fact]
    public void AoAbrir_MostraOsValoresAtuais()
    {
        _preferencias.Salvar(new Preferencias { MinutosAntes = 20 });
        _inicio.Definir(true);

        var vm = Criar();

        Assert.Equal(20, vm.MinutosAntes);
        Assert.True(vm.IniciarComWindows);
    }

    [Fact]
    public void AoAbrir_OEstadoDoInicioVemDoRegistroENaoDeUmaCopia()
    {
        var vm1 = Criar();
        Assert.False(vm1.IniciarComWindows);

        _registro.Gravar(InicioComWindowsService.NomeDoValor, "\"x.exe\""); // alguém ligou por fora

        Assert.True(Criar().IniciarComWindows);
    }

    [Fact]
    public void Salvar_GravaAsPreferenciasELigaOInicioComWindows()
    {
        var vm = Criar();
        vm.MinutosAntes = 25;
        vm.IniciarComWindows = true;

        vm.SalvarCommand.Execute(null);

        Assert.Equal(25, _preferencias.Atual.MinutosAntes);
        Assert.True(_inicio.Habilitado);
        Assert.Equal([true], _fechamentos);
    }

    [Fact]
    public void Salvar_DesligaOInicioComWindows()
    {
        _inicio.Definir(true);
        var vm = Criar();
        vm.IniciarComWindows = false;

        vm.SalvarCommand.Execute(null);

        Assert.False(_inicio.Habilitado);
        Assert.Empty(_registro.Valores);
    }

    [Fact]
    public void Salvar_SemMudarOInicio_NaoMexeNoRegistro()
    {
        var vm = Criar();
        var alteracoes = 0;
        _inicio.Alterado += () => alteracoes++;
        vm.MinutosAntes = 5;

        vm.SalvarCommand.Execute(null);

        Assert.Equal(0, alteracoes);
        Assert.Equal(5, _preferencias.Atual.MinutosAntes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1441)]
    public void Salvar_ComAntecedenciaInvalida_AvisaENaoGravaNemFecha(int minutos)
    {
        var vm = Criar();
        vm.MinutosAntes = minutos;
        vm.IniciarComWindows = true;

        vm.SalvarCommand.Execute(null);

        Assert.Contains("entre 0 e 1440", Assert.Single(_dialogos.Avisos));
        Assert.Equal(10, _preferencias.Atual.MinutosAntes);
        Assert.False(_inicio.Habilitado);
        Assert.Empty(_fechamentos);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1440)]
    public void Salvar_AceitaOsLimites(int minutos)
    {
        var vm = Criar();
        vm.MinutosAntes = minutos;

        vm.SalvarCommand.Execute(null);

        Assert.Equal(minutos, _preferencias.Atual.MinutosAntes);
        Assert.Equal([true], _fechamentos);
    }

    [Fact]
    public void Cancelar_NaoGravaNada()
    {
        var vm = Criar();
        vm.MinutosAntes = 99;
        vm.IniciarComWindows = true;

        vm.CancelarCommand.Execute(null);

        Assert.Equal(10, _preferencias.Atual.MinutosAntes);
        Assert.False(_inicio.Habilitado);
        Assert.Equal([false], _fechamentos);
    }

    [Fact]
    public void MostraOLocalDoArquivoDePreferencias()
    {
        Assert.EndsWith(Path.Combine("DateCheck", "preferencias.json"), Criar().LocalDoArquivo);
    }

    [Fact]
    public void MainViewModel_AbrirConfiguracoes_PedeAJanelaDeConfiguracoes()
    {
        var navegacao = new NavegacaoService(new ServiceCollection().BuildServiceProvider());
        var vm = new MainViewModel(navegacao, _dialogos);

        vm.AbrirConfiguracoesCommand.Execute(null);

        Assert.Equal(1, _dialogos.ConfiguracoesAbertas);
    }
}
