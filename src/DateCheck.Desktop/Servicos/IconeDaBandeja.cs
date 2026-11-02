using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;

namespace DateCheck.Desktop.Servicos;

/// <summary>
/// Ícone na bandeja do sistema (Hardcodet.NotifyIcon.Wpf). Menu: Abrir, Iniciar com o Windows (marcável) e Sair;
/// duplo clique abre a janela. Criado antes da inicialização do banco para o usuário ver que o app está vivo.
/// </summary>
public sealed class IconeDaBandeja : IDisposable
{
    private const string Nome = "DateCheck";

    private readonly IControleDaJanela _janela;
    private readonly IInicioComWindowsService _inicioComWindows;

    private TaskbarIcon? _icone;
    private MenuItem? _itemInicio;

    public IconeDaBandeja(IControleDaJanela janela, IInicioComWindowsService inicioComWindows)
    {
        _janela = janela;
        _inicioComWindows = inicioComWindows;
    }

    public void Criar()
    {
        var itemAbrir = new MenuItem { Header = "Abrir", FontWeight = FontWeights.SemiBold };
        itemAbrir.Click += (_, _) => _janela.Mostrar();

        _itemInicio = new MenuItem { Header = "Iniciar com o Windows", IsCheckable = true };
        _itemInicio.Click += (_, _) => _inicioComWindows.Definir(_itemInicio.IsChecked);

        var itemSair = new MenuItem { Header = "Sair" };
        itemSair.Click += (_, _) => _janela.Sair();

        var menu = new ContextMenu();
        menu.Items.Add(itemAbrir);
        menu.Items.Add(_itemInicio);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemSair);

        // O registro é a fonte da verdade: relê o estado toda vez que o menu abre.
        menu.Opened += (_, _) => _itemInicio.IsChecked = _inicioComWindows.Habilitado;
        _inicioComWindows.Alterado += () => _itemInicio.IsChecked = _inicioComWindows.Habilitado;

        _icone = new TaskbarIcon
        {
            ToolTipText = Nome,
            IconSource = CarregarIcone(),
            ContextMenu = menu
        };
        _icone.TrayMouseDoubleClick += (_, _) => _janela.Mostrar();
    }

    public void MostrarBalao(string titulo, string mensagem) =>
        _icone?.ShowBalloonTip(titulo, mensagem, BalloonIcon.Info);

    public void FecharBalao() => _icone?.CloseBalloon();

    public void Dispose()
    {
        // Sem isto o ícone fica "fantasma" na bandeja até o mouse passar por cima.
        _icone?.Dispose();
        _icone = null;
    }

    /// <summary>Usa o quadro de 32 px do .ico (o do tamanho mais próximo do usado pela bandeja).</summary>
    private static BitmapFrame CarregarIcone()
    {
        var decoder = BitmapDecoder.Create(
            new Uri("pack://application:,,,/Recursos/app.ico"),
            BitmapCreateOptions.None,
            BitmapCacheOption.OnLoad);

        return decoder.Frames.FirstOrDefault(f => f.PixelWidth == 32) ?? decoder.Frames[0];
    }
}
