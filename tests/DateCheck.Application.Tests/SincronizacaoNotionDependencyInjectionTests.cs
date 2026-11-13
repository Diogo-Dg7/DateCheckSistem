using DateCheck.Application.Interfaces;
using DateCheck.Application.Notion;
using DateCheck.Application.Servicos;
using DateCheck.Application.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DateCheck.Application.Tests;

public class SincronizacaoNotionDependencyInjectionTests
{
    [Fact]
    public void AddSincronizacaoNotion_PermiteResolverOsServicosComAsDependenciasExternas()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgendamentoRepository, FakeAgendamentoRepository>();
        services.AddSingleton<ICofreDeSegredos, CofreEmMemoria>();
        services.AddSingleton<IConfiguracaoNotion, ConfiguracaoNotionEmMemoria>();
        services.AddSingleton<INotionApi, NotionApiFalsa>();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddApplication();
        services.AddSincronizacaoNotion();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<INotionService>());
        Assert.NotNull(provider.GetRequiredService<IFonteDeAgendamentosExterna>());
        // A mesma instância atende o serviço de conexão e o segundo plano: o estado é um só.
        Assert.Same(provider.GetRequiredService<ISincronizacaoExternaService>(), provider.GetRequiredService<ISincronizacaoExternaService>());
    }
}
