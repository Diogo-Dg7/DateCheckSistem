using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DateCheck.Desktop.Diagnostico;

/// <summary>
/// Log em arquivo, um por dia (<c>datecheck-aaaaMMdd.log</c>) na pasta de dados do ambiente
/// (<c>%LOCALAPPDATA%\DateCheck\logs</c>, ou <c>DateCheck-Dev</c>). Sem pacote de terceiros: é o suficiente para
/// diagnosticar sincronizações e falhas de integração num app pessoal.
/// <list type="bullet">
/// <item>Categorias do app (<c>DateCheck.*</c>) gravam de Information para cima; as demais, só Warning para cima.</item>
/// <item>Rede de segurança: tokens (<c>ntn_…</c>, <c>secret_…</c>, <c>Bearer …</c>) são mascarados em TODA linha antes de
/// gravar, mesmo que alguém os registre por engano. O código nunca deve logar segredos; isto é a segunda barreira.</item>
/// <item>Escrever o log nunca derruba o app: qualquer falha de disco é engolida.</item>
/// <item>Guarda os últimos dias (padrão 7) e para de crescer um dia que passe de 5 MB.</item>
/// </list>
/// </summary>
public sealed partial class ArquivoDeLogProvider : ILoggerProvider
{
    public const string Mascara = "[segredo-removido]";
    public const long TamanhoMaximoDoDiaEmBytes = 5 * 1024 * 1024;

    private readonly string _pasta;
    private readonly TimeProvider _relogio;
    private readonly object _trava = new();

    public ArquivoDeLogProvider(string pasta, TimeProvider? relogio = null, int diasDeRetencao = 7)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        _pasta = pasta;
        _relogio = relogio ?? TimeProvider.System;
        LimparAntigos(diasDeRetencao);
    }

    public ILogger CreateLogger(string categoryName) => new Registrador(this, categoryName);

    public void Dispose()
    {
    }

    /// <summary>Caminho do arquivo do dia atual (para os testes e para dizer ao usuário onde olhar).</summary>
    public string CaminhoDeHoje() => CaminhoDoDia(_relogio.GetLocalNow().DateTime);

    private string CaminhoDoDia(DateTime dia) => Path.Combine(_pasta, $"datecheck-{dia:yyyyMMdd}.log");

    internal static bool Habilitado(string categoria, LogLevel nivel) =>
        nivel != LogLevel.None && nivel >= (categoria.StartsWith("DateCheck.", StringComparison.Ordinal)
            ? LogLevel.Information
            : LogLevel.Warning);

    /// <summary>Mascara os segredos de um texto (usado em toda linha gravada).</summary>
    public static string Mascarar(string texto) => PadraoDeSegredo().Replace(texto, Mascara);

    private void Escrever(LogLevel nivel, string categoria, string mensagem, Exception? excecao)
    {
        try
        {
            var agora = _relogio.GetLocalNow().DateTime;
            var linha = $"{agora:yyyy-MM-dd HH:mm:ss.fff} [{Nome(nivel)}] {categoria}: {UmaLinha(mensagem)}";
            if (excecao is not null)
                linha += $"{Environment.NewLine}    {UmaLinha(excecao.ToString())}";

            linha = Mascarar(linha) + Environment.NewLine;

            lock (_trava)
            {
                Directory.CreateDirectory(_pasta);
                var caminho = CaminhoDoDia(agora);
                if (File.Exists(caminho) && new FileInfo(caminho).Length > TamanhoMaximoDoDiaEmBytes)
                    return;

                File.AppendAllText(caminho, linha);
            }
        }
        catch (Exception)
        {
            // Log é auxiliar: disco cheio, pasta sem permissão ou arquivo aberto por outro programa não podem derrubar o app.
        }
    }

    private void LimparAntigos(int dias)
    {
        try
        {
            if (!Directory.Exists(_pasta))
                return;

            var limite = _relogio.GetLocalNow().DateTime.Date.AddDays(-dias);
            foreach (var arquivo in Directory.EnumerateFiles(_pasta, "datecheck-*.log"))
            {
                var nome = Path.GetFileNameWithoutExtension(arquivo)["datecheck-".Length..];
                if (DateTime.TryParseExact(nome, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dia) && dia < limite)
                    File.Delete(arquivo);
            }
        }
        catch (Exception)
        {
            // Limpeza é só higiene.
        }
    }

    private static string UmaLinha(string texto) => texto.Replace("\r\n", " | ").Replace('\n', ' ').Replace('\r', ' ');

    private static string Nome(LogLevel nivel) => nivel switch
    {
        LogLevel.Trace => "Trace",
        LogLevel.Debug => "Debug",
        LogLevel.Information => "Info",
        LogLevel.Warning => "Aviso",
        LogLevel.Error => "Erro",
        LogLevel.Critical => "Crítico",
        _ => nivel.ToString()
    };

    // Tokens de integração do Notion (ntn_…, secret_…) e qualquer cabeçalho "Bearer <token>".
    [GeneratedRegex(@"\b(?:ntn_|secret_)[A-Za-z0-9_\-]{8,}|Bearer\s+[A-Za-z0-9._~+/\-]{8,}=*", RegexOptions.IgnoreCase)]
    private static partial Regex PadraoDeSegredo();

    private sealed class Registrador : ILogger
    {
        private readonly ArquivoDeLogProvider _provider;
        private readonly string _categoria;

        public Registrador(ArquivoDeLogProvider provider, string categoria)
        {
            _provider = provider;
            _categoria = categoria;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => Habilitado(_categoria, logLevel);

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            _provider.Escrever(logLevel, _categoria, formatter(state, exception), exception);
        }
    }
}
