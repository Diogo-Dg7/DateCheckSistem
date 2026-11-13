using DateCheck.Application.Interfaces;
using DateCheck.Integracoes.Notion;
using DateCheck.Integracoes.Rede;
using DateCheck.Integracoes.Segredos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DateCheck.Integracoes;

public static class DependencyInjection
{
    /// <summary>Intervalo entre requisições ao Notion: ~2,9 por segundo, abaixo do limite de 3 por segundo.</summary>
    public static readonly TimeSpan IntervaloEntreRequisicoesDoNotion = TimeSpan.FromMilliseconds(350);

    /// <summary>
    /// Registra o cofre de segredos e o cliente do Notion. <paramref name="pastaDeDados"/> é a pasta de dados do
    /// ambiente (<c>%LOCALAPPDATA%\DateCheck</c> ou <c>DateCheck-Dev</c>); os segredos ficam em <c>segredos\</c> dentro dela.
    /// O <see cref="TimeProvider"/> do sistema é usado se nenhum outro for registrado.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static IServiceCollection AddIntegracoes(this IServiceCollection services, string pastaDeDados)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pastaDeDados);

        services.TryAddSingleton(TimeProvider.System);
        services.AddLogging();

        services.AddSingleton<ICofreDeSegredos>(_ => new CofreDpapi(Path.Combine(pastaDeDados, "segredos")));

        // O limitador é um só: o factory recria os handlers de tempos em tempos, e o ritmo não pode reiniciar junto.
        services.AddSingleton(sp => new LimitadorDeTaxa(sp.GetRequiredService<TimeProvider>(), IntervaloEntreRequisicoesDoNotion));

        services.AddHttpClient(NotionApi.NomeDoHttpClient, cliente =>
            {
                cliente.BaseAddress = new Uri("https://api.notion.com/");
                // Cobre as esperas de Retry-After; cada chamada ainda recebe o próprio CancellationToken.
                cliente.Timeout = TimeSpan.FromMinutes(5);
            })
            // Ordem: o reintento fica por fora, então cada nova tentativa também passa pelo limitador.
            .AddHttpMessageHandler(sp => new ReintentoHandler(sp.GetRequiredService<TimeProvider>()))
            .AddHttpMessageHandler(sp => new LimitadorDeTaxaHandler(sp.GetRequiredService<LimitadorDeTaxa>()));

        services.AddSingleton<ConversorDePaginaNotion>();
        services.AddSingleton<INotionApi, NotionApi>();

        return services;
    }
}
