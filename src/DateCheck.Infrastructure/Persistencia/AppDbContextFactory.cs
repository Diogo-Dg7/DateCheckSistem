using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DateCheck.Infrastructure.Persistencia;

/// <summary>Usada apenas pelas ferramentas do EF em tempo de design (dotnet ef migrations ...).</summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConexaoPadrao.ConnectionString)
            .Options;

        return new AppDbContext(options);
    }
}
