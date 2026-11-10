using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using DateCheck.Application;
using DateCheck.Application.Interfaces;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Integracoes;
using DateCheck.Desktop.Diagnostico;
using DateCheck.Desktop.Navegacao;
using DateCheck.Desktop.Notificacoes;
using DateCheck.Desktop.Servicos;
using DateCheck.Desktop.ViewModels;
using DateCheck.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DateCheck.Desktop;

// Qualificado porque o namespace DateCheck.Application oculta System.Windows.Application.
// Este arquivo é o composition root: o único lugar do Desktop que referencia tipos da Infrastructure.
public partial class App : System.Windows.Application
{
    private static readonly string TituloApp = AmbienteDeExecucao.Atual.TituloDoApp;

    private IHost? _host;

    // Em campos: se fossem variáveis locais, o coletor de lixo poderia liberar o Mutex e a trava sumiria.
    private InstanciaUnica? _instancia;
    private IconeDaBandeja? _bandeja;
    private GerenciadorDeToast? _toast;
    private ControleDaJanela? _controle;
    private ReacoesDaSincronizacao? _reacoes;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Sem janela aberta e, depois, com a janela escondida na bandeja: só encerramos por Sair/Shutdown.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Primeiríssimo passo, antes do Host e do banco: se já existe uma instância, avisa a primeira e sai.
        _instancia = new InstanciaUnica(AmbienteDeExecucao.Atual.NomeDaInstancia);
        if (!_instancia.TentarAssumir())
        {
            _instancia.AvisarInstanciaExistente();
            Shutdown();
            return;
        }

        ConfigurarCultura();
        DispatcherUnhandledException += AoOcorrerExcecaoNaoTratada;
        SessionEnding += AoEncerrarSessaoDoWindows;

        try
        {
            _host = CriarHost(e.Args);
            var services = _host.Services;

            services.GetRequiredService<ILogger<App>>().LogInformation(
                "{Titulo} iniciado. Ambiente: {Ambiente}. Pasta de dados: {Pasta}.",
                TituloApp, AmbienteDeExecucao.Atual.EhDesenvolvimento ? "Development" : "Production",
                AmbienteDeExecucao.Atual.PastaDeDados);

            _controle = services.GetRequiredService<ControleDaJanela>();
            _instancia.EscutarPedidosDeAbertura(_controle.Mostrar);

            // O ícone aparece antes do banco: com o Windows recém-ligado, o SQL Server pode demorar a subir.
            _bandeja = services.GetRequiredService<IconeDaBandeja>();
            _bandeja.Criar();
            _bandeja.MostrarBalao(TituloApp, "Iniciando…");

            IniciarToasts();

            if (!await InicializarBancoAsync(services, services.GetRequiredService<IDialogService>()))
            {
                Shutdown();
                return;
            }

            // Se o início com o Windows está ligado, mantém o caminho do registro apontando para este executável.
            services.GetRequiredService<InicioComWindowsService>().CorrigirCaminhoSeNecessario();

            // Criadas aqui, na thread da interface (capturam o contexto dela) e antes do segundo plano começar,
            // para já ouvirem a primeira sincronização.
            _reacoes = services.GetRequiredService<ReacoesDaSincronizacao>();

            await _host.StartAsync(); // inicia as notificações e a sincronização em segundo plano

            var janela = services.GetRequiredService<MainWindow>();
            MainWindow = janela;
            _controle.Registrar(janela);
            janela.Show();
            _bandeja.FecharBalao();
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
        // Ordem: tira os avisos da Central de Ações, remove o ícone (senão ele fica "fantasma"), para o Host
        // e só então solta a trava de instância única.
        _toast?.Encerrar();
        _bandeja?.Dispose();

        if (_host is not null)
        {
            Task.Run(() => _host.StopAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
            _host.Dispose();
        }

        _instancia?.Dispose();

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

        // Em desenvolvimento (DOTNET_ENVIRONMENT=Development) o Host já carrega appsettings.Development.json
        // (banco DateCheckDb_Dev); a trava abaixo impede, de qualquer jeito, o uso do banco real.
        var ambiente = AmbienteDeExecucao.Atual;
        ambiente.GarantirBancoIsolado(builder.Configuration[$"ConnectionStrings:{DateCheck.Infrastructure.Persistencia.ConexaoPadrao.Nome}"]);
        builder.Services.AddSingleton(ambiente);

        // Log em arquivo na pasta de dados do ambiente (logs\datecheck-aaaaMMdd.log), com tokens sempre mascarados.
        builder.Logging.AddProvider(new ArquivoDeLogProvider(Path.Combine(ambiente.PastaDeDados, "logs")));

        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IAbridorDeLinks, AbridorDeLinks>();

        // Comportamento de aplicativo: janela/bandeja, início com o Windows, preferências e notificações.
        builder.Services.AddSingleton<ControleDaJanela>();
        builder.Services.AddSingleton<IControleDaJanela>(sp => sp.GetRequiredService<ControleDaJanela>());
        builder.Services.AddSingleton<IconeDaBandeja>();

        builder.Services.AddSingleton(_ => ambiente.CriarRegistroDeInicializacao());
        builder.Services.AddSingleton(sp => new InicioComWindowsService(
            sp.GetRequiredService<IRegistroDeInicializacao>(), CaminhoDoExecutavel));
        builder.Services.AddSingleton<IInicioComWindowsService>(sp => sp.GetRequiredService<InicioComWindowsService>());

        builder.Services.AddSingleton<IArmazenamentoDePreferencias>(
            _ => new ArmazenamentoJsonDePreferencias(ArmazenamentoJsonDePreferencias.CaminhoPadrao(ambiente.PastaDeDados)));
        builder.Services.AddSingleton<IPreferenciasService, PreferenciasService>();

        builder.Services.AddSingleton<INotificador, AppNotificacaoNotificador>();
        builder.Services.AddHostedService<NotificacaoService>();

        // Integrações (opcionais: sem Notion configurado nada roda em segundo plano). O cofre de segredos e o resto
        // ficam na pasta de dados do ambiente: DateCheck em produção, DateCheck-Dev em desenvolvimento.
        DateCheck.Integracoes.DependencyInjection.AddIntegracoes(builder.Services, ambiente.PastaDeDados);
        builder.Services.AddSingleton<IConfiguracaoNotion, ConfiguracaoNotionDasPreferencias>();
        builder.Services.AddSincronizacaoNotion();
        builder.Services.AddSingleton<IAvisoDeBandeja>(sp => sp.GetRequiredService<IconeDaBandeja>());
        builder.Services.AddSingleton<ReacoesDaSincronizacao>();
        builder.Services.AddHostedService<SincronizacaoNotionBackgroundService>();

        // Telas.
        builder.Services.AddSingleton<NavegacaoService>();
        builder.Services.AddSingleton<INavegacao>(sp => sp.GetRequiredService<NavegacaoService>());
        builder.Services.AddSingleton<HojeViewModel>();
        builder.Services.AddSingleton<CalendarioViewModel>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddTransient<AgendamentoFormViewModel>();
        builder.Services.AddTransient<ConfiguracoesViewModel>();
        builder.Services.AddTransient<NotionConfiguracoesViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    /// <summary>Caminho do .exe (não o do dotnet.exe, caso o app seja iniciado por "dotnet DateCheck.Desktop.dll").</summary>
    private static string CaminhoDoExecutavel()
    {
        var caminho = Environment.ProcessPath;
        if (caminho is not null && !Path.GetFileNameWithoutExtension(caminho).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return caminho;

        return Path.ChangeExtension(Assembly.GetEntryAssembly()!.Location, ".exe");
    }

    /// <summary>
    /// Registra o app para receber cliques nos toasts. Se falhar (por exemplo, ambiente sem suporte),
    /// o aplicativo continua funcionando, só sem a ativação por clique.
    /// </summary>
    private void IniciarToasts()
    {
        try
        {
            _toast = new GerenciadorDeToast();
            _toast.Iniciar(_controle!.MostrarHoje);
        }
        catch (Exception)
        {
            _toast = null;
        }
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

    /// <summary>Sem isto, esconder a janela no "fechar" bloquearia o desligamento/logoff do Windows.</summary>
    private void AoEncerrarSessaoDoWindows(object sender, SessionEndingCancelEventArgs e) =>
        _controle?.PermitirFechamento();

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
