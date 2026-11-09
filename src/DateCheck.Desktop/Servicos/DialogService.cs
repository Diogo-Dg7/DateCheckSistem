using System.Windows;
using DateCheck.Desktop.ViewModels;
using DateCheck.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DateCheck.Desktop.Servicos;

public sealed class DialogService : IDialogService
{
    private static readonly string Titulo = AmbienteDeExecucao.Atual.TituloDoApp;

    private readonly IServiceProvider _services;

    public DialogService(IServiceProvider services)
    {
        _services = services;
    }

    public void MostrarErro(string mensagem) =>
        MessageBox.Show(Dono(), mensagem, Titulo, MessageBoxButton.OK, MessageBoxImage.Error);

    public void MostrarAviso(string mensagem) =>
        MessageBox.Show(Dono(), mensagem, Titulo, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirmar(string titulo, string mensagem) =>
        MessageBox.Show(Dono(), mensagem, titulo, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool PerguntarTentarNovamente(string titulo, string mensagem) =>
        MessageBox.Show(Dono(), mensagem, titulo, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public bool AbrirFormulario(int? agendamentoId, DateTime? diaSugerido = null)
    {
        // Um ViewModel novo por abertura, para o formulário sempre começar limpo.
        var viewModel = _services.GetRequiredService<AgendamentoFormViewModel>();
        viewModel.Preparar(agendamentoId, diaSugerido);

        var janela = new AgendamentoFormWindow(viewModel) { Owner = Dono() };
        return janela.ShowDialog() == true;
    }

    public bool AbrirConfiguracoes()
    {
        var viewModel = _services.GetRequiredService<ConfiguracoesViewModel>();

        var janela = new ConfiguracoesWindow(viewModel) { Owner = Dono() };
        return janela.ShowDialog() == true;
    }

    public DateTime? PedirDataHora(string titulo, string rotulo, DateTime inicial)
    {
        var viewModel = new DataHoraViewModel(this, titulo, rotulo, inicial);

        var janela = new DataHoraWindow(viewModel) { Owner = Dono() };
        return janela.ShowDialog() == true ? viewModel.Resultado : null;
    }

    /// <summary>Janela ativa, para o diálogo ficar sobre ela (nula durante a inicialização).</summary>
    private static Window? Dono() =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;
}
