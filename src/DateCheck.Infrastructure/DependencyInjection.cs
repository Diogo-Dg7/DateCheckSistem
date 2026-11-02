using DateCheck.Application.Interfaces;
using DateCheck.Infrastructure.Persistencia;
using DateCheck.Infrastructure.Repositorios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DateCheck.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra a factory do contexto (nunca um contexto de vida longa) e o repositório.
    /// A connection string vem de <c>ConnectionStrings:DateCheckDb</c>.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConexaoPadrao.Nome)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConexaoPadrao.Nome}' não encontrada na configuração.");

        // Retry porque o app abre junto com o Windows, quando o serviço do SQL Server ainda pode estar subindo.
        services.AddDbContextFactory<AppDbContext>(opcoes =>
            opcoes.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

        services.AddSingleton<IAgendamentoRepository, AgendamentoRepository>();

        return services;
    }

    /// <summary>Cria o banco se necessário e aplica as migrations pendentes.</summary>
    public static async Task AplicarMigrationsAsync(
        this IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using var contexto = await factory.CreateDbContextAsync(cancellationToken);
        await contexto.Database.MigrateAsync(cancellationToken);
    }
}
