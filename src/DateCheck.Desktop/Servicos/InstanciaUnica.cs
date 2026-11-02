namespace DateCheck.Desktop.Servicos;

/// <summary>
/// Garante uma única instância do aplicativo por sessão do Windows, com um Mutex nomeado. A segunda execução
/// avisa a primeira por um evento nomeado (para trazer a janela à frente) e encerra.
/// </summary>
public sealed class InstanciaUnica : IDisposable
{
    private readonly string _nomeDoMutex;
    private readonly string _nomeDoEvento;

    private Mutex? _mutex;
    private EventWaitHandle? _evento;
    private RegisteredWaitHandle? _espera;

    public InstanciaUnica(string nome = "DateCheckSistem")
    {
        // "Local\" = por sessão de usuário (dois usuários logados podem ter cada um a sua instância).
        _nomeDoMutex = $@"Local\{nome}.Instancia";
        _nomeDoEvento = $@"Local\{nome}.Abrir";
    }

    /// <summary>Tenta ser a instância principal; retorna false se já existe outra rodando.</summary>
    public bool TentarAssumir()
    {
        var mutex = new Mutex(initiallyOwned: true, _nomeDoMutex, out var primeira);
        if (!primeira)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        _evento = new EventWaitHandle(false, EventResetMode.AutoReset, _nomeDoEvento);
        return true;
    }

    /// <summary>
    /// Pede à instância principal que se mostre. Chamado pela segunda execução antes de encerrar.
    /// Retorna false se não foi possível alcançar a outra instância.
    /// </summary>
    public bool AvisarInstanciaExistente()
    {
        try
        {
            using var evento = EventWaitHandle.OpenExisting(_nomeDoEvento);
            return evento.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Passa a escutar os pedidos de abertura vindos de outras execuções. O retorno é chamado em
    /// uma thread do pool: quem usa deve voltar para a thread da interface.
    /// </summary>
    public void EscutarPedidosDeAbertura(Action aoReceber)
    {
        if (_evento is null)
            throw new InvalidOperationException("Chame TentarAssumir() com sucesso antes de escutar pedidos.");

        _espera = ThreadPool.RegisterWaitForSingleObject(
            _evento, (_, _) => aoReceber(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _espera?.Unregister(null);
        _evento?.Dispose();

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Liberado por outra thread: o sistema o libera ao encerrar o processo.
            }

            _mutex.Dispose();
        }
    }
}
