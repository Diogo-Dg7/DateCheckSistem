using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using DateCheck.Application;
using DateCheck.Desktop.Servicos;
using DateCheck.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DateCheck.Desktop;

// Qualificado porque o namespace DateCheck.Application oculta System.Windows.Application.
// Este arquivo é o composition root: o único lugar do Desktop que referencia tipos da Infrastructure.
public partial class App : System.Windows.Application
{
    private const string TituloApp = "DateCheck";

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Sem janela aberta ainda: só encerra quando pedirmos (ou quando a janela principal fechar, mais abaixo).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ConfigurarCultura();
        DispatcherUnhandledException += AoOcorrerExcecaoNaoTratada;

        try
        {
            _host = CriarHost(e.Args);

            var dialogos = _host.Services.GetRequiredService<IDialogService>();
            if (!await InicializarBancoAsync(_host.Services, dialogos))
            {
                Shutdown();
                return;
            }

            await _host.StartAsync();

            var janela = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = janela;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            janela.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível iniciar o aplicativo.\n\n{ex.GetBaseException().Message}",
                TituloApp, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            Task.Run(() => _host.StopAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static IHost CriarHost(string[] args)
    {
        // ContentRoot = pasta do executável: ao iniciar com o Windows o diretório atual não é o do app.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    /// <summary>
    /// Aplica as migrations antes de abrir a janela. O retry do EF já espera o SQL Server subir; se mesmo
    /// assim falhar, o usuário escolhe entre tentar de novo e sair.
    /// </summary>
    private static async Task<bool> InicializarBancoAsync(IServiceProvider services, IDialogService dialogos)
    {
        while (true)
        {
            try
            {
                await services.AplicarMigrationsAsync();
                return true;
            }
            catch (Exception ex)
            {
                var tentarNovamente = dialogos.PerguntarTentarNovamente(
                    "Banco de dados indisponível",
                    "Não foi possível acessar o banco de dados.\n\n" +
                    "O SQL Server (instância .\\SQLEXPRESS) pode ainda estar iniciando. " +
                    "Aguarde um instante e tente novamente.\n\n" +
                    $"Detalhe: {ex.GetBaseException().Message}\n\n" +
                    "Sim = tentar novamente    Não = sair");

                if (!tentarNovamente)
                    return false;
            }
        }
    }

    private static void ConfigurarCultura()
    {
        var cultura = new CultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = cultura;
        CultureInfo.DefaultThreadCurrentUICulture = cultura;
        Thread.CurrentThread.CurrentCulture = cultura;
        Thread.CurrentThread.CurrentUICulture = cultura;

        // Sem isto, os bindings do WPF formatam datas em en-US, ignorando a cultura do sistema.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(cultura.IetfLanguageTag)));
    }

    private void AoOcorrerExcecaoNaoTratada(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        var mensagem = $"Ocorreu um erro inesperado.\n\n{e.Exception.GetBaseException().Message}";
        if (_host?.Services.GetService<IDialogService>() is { } dialogos)
            dialogos.MostrarErro(mensagem);
        else
            MessageBox.Show(mensagem, TituloApp, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
