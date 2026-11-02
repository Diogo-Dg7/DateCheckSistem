using System.Windows;
using DateCheck.Desktop.ViewModels;

namespace DateCheck.Desktop.Views;

public partial class ConfiguracoesWindow : Window
{
    public ConfiguracoesWindow(ConfiguracoesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.FechamentoSolicitado += salvou => DialogResult = salvou;
    }
}
