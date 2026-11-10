using System.ComponentModel;
using System.Windows;
using DateCheck.Desktop.ViewModels;

namespace DateCheck.Desktop.Views;

public partial class ConfiguracoesWindow : Window
{
    public ConfiguracoesWindow(ConfiguracoesViewModel geral, NotionConfiguracoesViewModel notion)
    {
        InitializeComponent();
        DataContext = geral;
        AbaNotion.DataContext = notion;

        geral.FechamentoSolicitado += salvou => DialogResult = salvou;

        // PasswordBox não aceita binding (de propósito): o token passa da caixa para o ViewModel só em memória.
        CampoToken.PasswordChanged += (_, _) => notion.Token = CampoToken.Password;
        notion.PropertyChanged += (_, e) => AoMudarPropriedadeDoNotion(notion, e);

        Closed += (_, _) =>
        {
            CampoToken.Clear();
            notion.Token = string.Empty;
            notion.Dispose();
        };
    }

    private void AoMudarPropriedadeDoNotion(NotionConfiguracoesViewModel notion, PropertyChangedEventArgs e)
    {
        // Depois de conectar, o ViewModel apaga o token: a caixa também precisa esvaziar.
        if (e.PropertyName == nameof(NotionConfiguracoesViewModel.Token) && notion.Token.Length == 0)
            CampoToken.Clear();
    }
}
