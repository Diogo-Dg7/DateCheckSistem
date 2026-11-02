using DateCheck.Application.Servicos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DateCheck.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra os serviços da aplicação. Quem chama precisa registrar <c>IAgendamentoRepository</c>;
    /// o <see cref="TimeProvider"/> do sistema é usado se nenhum outro for registrado.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IConflitoService, ConflitoService>();
        services.AddSingleton<IAgendamentoService, AgendamentoService>();
        services.AddSingleton<IDuplicacaoService, DuplicacaoService>();
        services.AddSingleton<IRepeticaoService, RepeticaoService>();

        return services;
    }
}
