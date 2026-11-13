using DateCheck.Application.Interfaces;
using DateCheck.Application.Notion;
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

    /// <summary>
    /// Registra a sincronização com o Notion. Quem chama precisa registrar <c>ICofreDeSegredos</c>,
    /// <c>INotionApi</c>, <c>IConfiguracaoNotion</c> e <c>IAgendamentoRepository</c> (e <c>AddApplication</c>).
    /// </summary>
    public static IServiceCollection AddSincronizacaoNotion(this IServiceCollection services)
    {
        services.AddSingleton<IFonteDeAgendamentosExterna, NotionFonte>();
        services.AddSingleton<ISincronizacaoExternaService, SincronizacaoExternaService>();
        services.AddSingleton<INotionService, NotionService>();

        return services;
    }
}
