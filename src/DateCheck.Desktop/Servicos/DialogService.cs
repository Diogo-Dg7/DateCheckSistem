using System.Windows;

namespace DateCheck.Desktop.Servicos;

public sealed class DialogService : IDialogService
{
    private const string Titulo = "DateCheck";

    public void MostrarErro(string mensagem) =>
        MessageBox.Show(Dono(), mensagem, Titulo, MessageBoxButton.OK, MessageBoxImage.Error);

    public void MostrarAviso(string mensagem) =>
        MessageBox.Show(Dono(), mensagem, Titulo, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirmar(string titulo, string mensagem) =>
        MessageBox.Show(Dono(), mensagem, titulo, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool PerguntarTentarNovamente(string titulo, string mensagem) =>
        MessageBox.Show(Dono(), mensagem, titulo, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <summary>Janela ativa, para o diálogo ficar sobre ela (nula durante a inicialização).</summary>
    internal static Window? Dono() =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;
}
