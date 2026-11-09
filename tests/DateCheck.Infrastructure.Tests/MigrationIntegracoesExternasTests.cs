using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using DateCheck.Infrastructure.Persistencia;

namespace DateCheck.Infrastructure.Tests;

/// <summary>
/// Aplica a migration inicial num banco próprio, grava um registro "antigo" (sem as colunas novas) e só então migra
/// até a última versão, como acontece com quem já usava a v0.1.0.
/// </summary>
[Trait("Categoria", "Integracao")]
public class MigrationIntegracoesExternasTests : IAsyncLifetime
{
    private const string MigrationInicial = "20261102154004_Inicial";

    private readonly string _nomeDoBanco = $"DateCheckDb_Tests_{Guid.NewGuid():N}";
    private string _connectionString = string.Empty;

    public Task InitializeAsync()
    {
        var conexaoBase = Environment.GetEnvironmentVariable(BancoDeDadosFixture.VariavelDeAmbiente);
        var builder = new SqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(conexaoBase)
                ? @"Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True;"
                : conexaoBase)
        {
            InitialCatalog = _nomeDoBanco
        };
        _connectionString = builder.ConnectionString;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await using var contexto = CriarContexto();
        await contexto.Database.EnsureDeletedAsync();
    }

    private AppDbContext CriarContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options);

    [Fact]
    public async Task RegistrosAntigos_ViramLocalSemDiaInteiroENemIdExterno()
    {
        await using var contexto = CriarContexto();
        var migrator = contexto.GetService<IMigrator>();

        await migrator.MigrateAsync(MigrationInicial);
        await contexto.Database.ExecuteSqlRawAsync("""
            INSERT INTO dbo.Agendamentos (Titulo, Descricao, Inicio, Fim, Status, SerieId)
            VALUES (N'Antigo 1', NULL, '2026-11-02T09:00:00', '2026-11-02T10:00:00', 0, NULL),
                   (N'Antigo 2', N'desc', '2026-11-03T09:00:00', '2026-11-03T10:00:00', 1, NEWID())
            """);

        await migrator.MigrateAsync();

        var itens = await contexto.Agendamentos.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, itens.Count);
        Assert.All(itens, item =>
        {
            Assert.Equal(Domain.Enums.OrigemAgendamento.Local, item.Origem);
            Assert.False(item.DiaInteiro);
            Assert.Null(item.IdExterno);
            Assert.Null(item.UrlExterna);
        });
        Assert.Equal(["Antigo 1", "Antigo 2"], itens.Select(i => i.Titulo));
        Assert.Equal(Domain.Enums.StatusAgendamento.Concluido, itens[1].Status);
    }

    [Fact]
    public async Task IndiceDeOrigemEIdExterno_EUnicoEFiltradoPorIdExternoNaoNulo()
    {
        await using var contexto = CriarContexto();
        await contexto.Database.MigrateAsync();

        var indice = (await contexto.Database
            .SqlQuery<string>($"""
                SELECT CONCAT(i.is_unique, ':', i.filter_definition) AS Value
                FROM sys.indexes i
                WHERE i.object_id = OBJECT_ID('dbo.Agendamentos') AND i.name = 'IX_Agendamentos_Origem_IdExterno'
                """)
            .ToListAsync()).Single();

        Assert.Equal("1:([IdExterno] IS NOT NULL)", indice);
    }
}
