namespace DateCheck.Infrastructure.Persistencia;

public static class ConexaoPadrao
{
    /// <summary>Nome da connection string em <c>appsettings.json</c> (seção ConnectionStrings).</summary>
    public const string Nome = "DateCheckDb";

    /// <summary>Instância local do SQL Express; sem credenciais, então não há segredo aqui.</summary>
    public const string ConnectionString =
        @"Server=.\SQLEXPRESS;Database=DateCheckDb;Trusted_Connection=True;TrustServerCertificate=True;";
}
