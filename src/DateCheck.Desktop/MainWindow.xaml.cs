using System.Windows;
using DateCheck.Desktop.ViewModels;

namespace DateCheck.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Abre na tela Hoje assim que a janela é exibida.
        Loaded += async (_, _) => await viewModel.InicializarAsync();
    }
}
