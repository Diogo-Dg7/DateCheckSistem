using DateCheck.Application.Interfaces;
using DateCheck.Application.Notion;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Application.Tests.Fakes;

/// <summary>Cofre em memória: nenhum segredo vai para o disco nos testes.</summary>
public sealed class CofreEmMemoria : ICofreDeSegredos
{
    private readonly Dictionary<string, string> _segredos = [];

    public string? Ler(string nome) => _segredos.GetValueOrDefault(nome);

    public void Gravar(string nome, string valor) => _segredos[nome] = valor;

    public void Apagar(string nome) => _segredos.Remove(nome);

    public bool Contem(string nome) => _segredos.ContainsKey(nome);
}

public sealed class ConfiguracaoNotionEmMemoria : IConfiguracaoNotion
{
    public ConfiguracaoNotion? Atual { get; set; }

    public ConfiguracaoNotion? Obter() => Atual;

    public void Salvar(ConfiguracaoNotion configuracao) => Atual = configuracao;

    public void Limpar() => Atual = null;
}

/// <summary>Fonte externa roteirizada: devolve uma lista, falha, ou fica parada até ser liberada.</summary>
public sealed class FonteFalsa : IFonteDeAgendamentosExterna
{
    public OrigemAgendamento Origem => OrigemAgendamento.Notion;

    public bool EstaConfigurada { get; set; } = true;

    public List<Agendamento> Itens { get; set; } = [];

    public Exception? Falha { get; set; }

    /// <summary>Se definido, a consulta espera por ele (para simular uma sincronização demorada).</summary>
    public TaskCompletionSource? Liberacao { get; set; }

    public List<(DateTime Inicio, DateTime Fim)> Chamadas { get; } = [];

    public async Task<IReadOnlyList<Agendamento>> ListarAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        Chamadas.Add((inicio, fim));

        if (Liberacao is { } liberacao)
            await liberacao.Task.WaitAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (Falha is not null)
            throw Falha;

        return Itens.ToList();
    }
}

public sealed class NotionApiFalsa : INotionApi
{
    public string Identidade { get; set; } = "DateCheck (Casa)";

    public EstruturaDoBancoNotion? Estrutura { get; set; }

    public IReadOnlyList<Agendamento> Paginas { get; set; } = [];

    public Exception? Falha { get; set; }

    public List<string> TokensRecebidos { get; } = [];

    public List<(ConfiguracaoNotion Configuracao, DateOnly De, DateOnly Ate)> Consultas { get; } = [];

    public int ChamadasDeEstrutura { get; private set; }

    public Task<string> ObterIdentidadeAsync(string token, CancellationToken cancellationToken = default)
    {
        TokensRecebidos.Add(token);
        return Falha is null ? Task.FromResult(Identidade) : Task.FromException<string>(Falha);
    }

    public Task<EstruturaDoBancoNotion> ObterEstruturaAsync(
        string token, string databaseId, CancellationToken cancellationToken = default)
    {
        TokensRecebidos.Add(token);
        ChamadasDeEstrutura++;
        if (Falha is not null)
            return Task.FromException<EstruturaDoBancoNotion>(Falha);

        return Task.FromResult((Estrutura ?? throw new InvalidOperationException("Estrutura não configurada")) with { DatabaseId = databaseId });
    }

    public Task<IReadOnlyList<Agendamento>> ConsultarAsync(
        string token, ConfiguracaoNotion configuracao, DateOnly de, DateOnly ate, CancellationToken cancellationToken = default)
    {
        TokensRecebidos.Add(token);
        Consultas.Add((configuracao, de, ate));
        return Falha is null ? Task.FromResult(Paginas) : Task.FromException<IReadOnlyList<Agendamento>>(Falha);
    }
}

/// <summary>Logger que guarda o texto formatado das mensagens para os testes conferirem o que foi registrado.</summary>
public sealed class LogCapturado<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public List<(Microsoft.Extensions.Logging.LogLevel Nivel, string Mensagem)> Entradas { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entradas.Add((logLevel, formatter(state, exception)));
}
