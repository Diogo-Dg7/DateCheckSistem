using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DateCheck.Integracoes.Tests.Apoio;

/// <summary>Lê os JSON de exemplo (respostas fictícias da API) copiados para a saída dos testes.</summary>
public static class Exemplos
{
    public static string Texto(string nome) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Exemplos", "notion", nome));

    public static JsonElement Json(string nome)
    {
        using var documento = JsonDocument.Parse(Texto(nome));
        return documento.RootElement.Clone();
    }
}

/// <summary>Logger que guarda as mensagens para os testes conferirem avisos (e a ausência de segredos).</summary>
public sealed class LogCapturado<T> : ILogger<T>
{
    public List<(LogLevel Nivel, string Mensagem)> Entradas { get; } = [];

    public IEnumerable<string> Avisos => Entradas.Where(e => e.Nivel == LogLevel.Warning).Select(e => e.Mensagem);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var texto = formatter(state, exception);
        if (exception is not null)
            texto += " | " + exception;
        Entradas.Add((logLevel, texto));
    }
}
