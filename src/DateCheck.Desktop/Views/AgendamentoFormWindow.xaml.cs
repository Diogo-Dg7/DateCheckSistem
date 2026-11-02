using System.Windows;
using DateCheck.Desktop.ViewModels;

namespace DateCheck.Desktop.Views;

public partial class AgendamentoFormWindow : Window
{
    public AgendamentoFormWindow(AgendamentoFormViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.FechamentoSolicitado += salvou => DialogResult = salvou;
        Loaded += async (_, _) => await viewModel.CarregarAsync();
    }
}
