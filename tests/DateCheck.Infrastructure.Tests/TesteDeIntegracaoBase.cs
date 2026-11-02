using DateCheck.Application.Interfaces;
using DateCheck.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DateCheck.Infrastructure.Tests;

[Collection(BancoDeDadosCollection.Nome)]
[Trait("Categoria", "Integracao")]
public abstract class TesteDeIntegracaoBase : IAsyncLifetime
{
    protected TesteDeIntegracaoBase(BancoDeDadosFixture banco)
    {
        Banco = banco;
    }

    protected BancoDeDadosFixture Banco { get; }

    protected IAgendamentoRepository Repositorio =>
        Banco.Services.GetRequiredService<IAgendamentoRepository>();

    public Task InitializeAsync() => Banco.LimparAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected async Task<TResultado> ComContextoAsync<TResultado>(Func<AppDbContext, Task<TResultado>> consulta)
    {
        var factory = Banco.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var contexto = await factory.CreateDbContextAsync();
        return await consulta(contexto);
    }
}
