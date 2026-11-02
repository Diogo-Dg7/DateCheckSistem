using DateCheck.Application.Interfaces;
using DateCheck.Application.Servicos;
using DateCheck.Application.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Application.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_PermiteResolverTodosOsServicos()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgendamentoRepository, FakeAgendamentoRepository>();
        services.AddApplication();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<IAgendamentoService>());
        Assert.NotNull(provider.GetRequiredService<IConflitoService>());
        Assert.NotNull(provider.GetRequiredService<IDuplicacaoService>());
        Assert.NotNull(provider.GetRequiredService<IRepeticaoService>());
    }

    [Fact]
    public void AddApplication_UsaTimeProviderDoSistemaPorPadrao()
    {
        var services = new ServiceCollection().AddApplication();

        using var provider = services.BuildServiceProvider();

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddApplication_RespeitaTimeProviderJaRegistrado()
    {
        var relogio = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(relogio);
        services.AddApplication();

        using var provider = services.BuildServiceProvider();

        Assert.Same(relogio, provider.GetRequiredService<TimeProvider>());
    }
}
