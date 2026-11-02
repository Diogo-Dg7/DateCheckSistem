using System.Windows;
using DateCheck.Desktop.ViewModels;

namespace DateCheck.Desktop.Views;

public partial class DataHoraWindow : Window
{
    public DataHoraWindow(DataHoraViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.FechamentoSolicitado += confirmou => DialogResult = confirmou;
    }
}
