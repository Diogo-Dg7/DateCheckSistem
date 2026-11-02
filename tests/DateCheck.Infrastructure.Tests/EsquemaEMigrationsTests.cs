using DateCheck.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DateCheck.Infrastructure.Tests;

public class EsquemaEMigrationsTests : TesteDeIntegracaoBase
{
    public EsquemaEMigrationsTests(BancoDeDadosFixture banco) : base(banco)
    {
    }

    [Fact]
    public async Task Migration_CriaIndicesEmInicioESerieId()
    {
        var indices = await ComContextoAsync(c => c.Database
            .SqlQuery<string>($"""
                SELECT i.name AS Value
                FROM sys.indexes i
                WHERE i.object_id = OBJECT_ID('dbo.Agendamentos') AND i.name IS NOT NULL
                """)
            .ToListAsync());

        Assert.Contains("IX_Agendamentos_Inicio", indices);
        Assert.Contains("IX_Agendamentos_SerieId", indices);
    }

    [Fact]
    public async Task Migration_UsaDatetime2ETamanhosDasConstantes()
    {
        var colunas = await ComContextoAsync(c => c.Database
            .SqlQuery<string>($"""
                SELECT CONCAT(COLUMN_NAME, ':', DATA_TYPE, ':', COALESCE(CHARACTER_MAXIMUM_LENGTH, 0)) AS Value
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Agendamentos'
                """)
            .ToListAsync());

        Assert.Contains("Inicio:datetime2:0", colunas);
        Assert.Contains("Fim:datetime2:0", colunas);
        Assert.Contains("Titulo:nvarchar:200", colunas);
        Assert.Contains("Descricao:nvarchar:2000", colunas);
    }

    [Fact]
    public async Task AplicarMigrations_PodeSerChamadaVariasVezes()
    {
        await Banco.Services.AplicarMigrationsAsync();
        await Banco.Services.AplicarMigrationsAsync();

        var pendentes = await ComContextoAsync(async c => (await c.Database.GetPendingMigrationsAsync()).ToList());
        Assert.Empty(pendentes);
    }
}
