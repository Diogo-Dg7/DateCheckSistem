using DateCheck.Application;
using DateCheck.Infrastructure;
using DateCheck.Infrastructure.Persistencia;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DateCheck.Infrastructure.Tests;

/// <summary>
/// Cria um banco com nome único no SQL Server Express, aplica as migrations de verdade e o apaga ao fim.
/// A connection string base pode ser trocada pela variável de ambiente <see cref="VariavelDeAmbiente"/>
/// (o nome do banco é sempre substituído por um nome único).
/// </summary>
public sealed class BancoDeDadosFixture : IAsyncLifetime
{
    public const string VariavelDeAmbiente = "DATECHECK_TEST_CONNECTION";

    private const string ConexaoBasePadrao =
        @"Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True;";

    private ServiceProvider? _provider;

    public string NomeDoBanco { get; } = $"DateCheckDb_Tests_{Guid.NewGuid():N}";

    public string ConnectionString { get; private set; } = string.Empty;

    public IServiceProvider Services =>
        _provider ?? throw new InvalidOperationException("Fixture não inicializada.");

    public async Task InitializeAsync()
    {
        var conexaoBase = Environment.GetEnvironmentVariable(VariavelDeAmbiente);
        var builder = new SqlConnectionStringBuilder(string.IsNullOrWhiteSpace(conexaoBase) ? ConexaoBasePadrao : conexaoBase)
        {
            InitialCatalog = NomeDoBanco
        };
        ConnectionString = builder.ConnectionString;

        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{ConexaoPadrao.Nome}"] = ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuracao);
        services.AddApplication();
        _provider = services.BuildServiceProvider();

        await _provider.AplicarMigrationsAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is null)
            return;

        var factory = _provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var contexto = await factory.CreateDbContextAsync())
        {
            await contexto.Database.EnsureDeletedAsync();
        }

        await _provider.DisposeAsync();
    }

    /// <summary>Esvazia a tabela para que cada teste comece do zero.</summary>
    public async Task LimparAsync()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var contexto = await factory.CreateDbContextAsync();
        await contexto.Agendamentos.ExecuteDeleteAsync();
    }
}

[CollectionDefinition(Nome)]
public sealed class BancoDeDadosCollection : ICollectionFixture<BancoDeDadosFixture>
{
    public const string Nome = "Banco de dados";
}
