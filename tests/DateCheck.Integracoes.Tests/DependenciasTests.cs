namespace DateCheck.Integracoes.Tests;

public class DependenciasTests
{
    [Fact]
    public void Integracoes_ReferenciaSoAApplicationEDomain_NuncaInfrastructureNemDesktop()
    {
        var referencias = typeof(DateCheck.Integracoes.AssemblyMarcador).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("DateCheck.", StringComparison.Ordinal))
            .ToList();

        Assert.Contains("DateCheck.Application", referencias);
        Assert.DoesNotContain("DateCheck.Infrastructure", referencias);
        Assert.DoesNotContain("DateCheck.Desktop", referencias);
    }

    [Fact]
    public void Application_NaoConheceAIntegracoes()
    {
        var referencias = typeof(DateCheck.Application.Interfaces.INotionApi).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!);

        Assert.DoesNotContain("DateCheck.Integracoes", referencias);
    }
}
