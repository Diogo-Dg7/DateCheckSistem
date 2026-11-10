using System.Reflection;
using DateCheck.Application.Dtos;
using DateCheck.Application.Interfaces;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Notificacoes;
using DateCheck.Desktop.Navegacao;
using DateCheck.Desktop.Servicos;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Desktop.Tests.Apoio;

/// <summary>Registra o que foi mostrado ao usuário e responde às perguntas conforme configurado.</summary>
public sealed class FakeDialogService : IDialogService
{
    public List<string> Erros { get; } = [];

    public List<string> Avisos { get; } = [];

    public List<(string Titulo, string Mensagem)> Confirmacoes { get; } = [];

    public List<(int? Id, DateTime? Dia)> FormulariosAbertos { get; } = [];

    public List<(string Titulo, string Rotulo, DateTime Inicial)> DatasPedidas { get; } = [];

    /// <summary>Resposta para toda pergunta Sim/Não (padrão: sim).</summary>
    public bool RespostaConfirmar { get; set; } = true;

    public bool ResultadoDoFormulario { get; set; }

    public int ConfiguracoesAbertas { get; private set; }

    public bool ResultadoDasConfiguracoes { get; set; }

    public DateTime? DataEscolhida { get; set; }

    public void MostrarErro(string mensagem) => Erros.Add(mensagem);

    public void MostrarAviso(string mensagem) => Avisos.Add(mensagem);

    public bool Confirmar(string titulo, string mensagem)
    {
        Confirmacoes.Add((titulo, mensagem));
        return RespostaConfirmar;
    }

    public bool PerguntarTentarNovamente(string titulo, string mensagem) => false;

    public bool AbrirFormulario(int? agendamentoId, DateTime? diaSugerido = null)
    {
        FormulariosAbertos.Add((agendamentoId, diaSugerido));
        return ResultadoDoFormulario;
    }

    public bool AbrirConfiguracoes()
    {
        ConfiguracoesAbertas++;
        return ResultadoDasConfiguracoes;
    }

    public DateTime? PedirDataHora(string titulo, string rotulo, DateTime inicial)
    {
        DatasPedidas.Add((titulo, rotulo, inicial));
        return DataEscolhida;
    }
}

/// <summary>Registra os links "abertos" sem iniciar nenhum navegador.</summary>
public sealed class FakeAbridorDeLinks : IAbridorDeLinks
{
    public List<string> Abertos { get; } = [];

    /// <summary>Simula um link inválido ou a falha do sistema ao abri-lo.</summary>
    public bool Resultado { get; set; } = true;

    public bool Abrir(string url)
    {
        Abertos.Add(url);
        return Resultado;
    }
}

public sealed class FakeNavegacao : INavegacao
{
    public List<DateTime?> HojeMostrado { get; } = [];

    public int CalendarioMostrado { get; private set; }

    public int Recarregamentos { get; private set; }

    public Task RecarregarAtualAsync()
    {
        Recarregamentos++;
        return Task.CompletedTask;
    }

    public Task MostrarHojeAsync(DateTime? dia = null)
    {
        HojeMostrado.Add(dia);
        return Task.CompletedTask;
    }

    public Task MostrarCalendarioAsync()
    {
        CalendarioMostrado++;
        return Task.CompletedTask;
    }
}

/// <summary>Repositório em memória; as operações são síncronas, então os testes são determinísticos.</summary>
public sealed class FakeAgendamentoRepository : IAgendamentoRepository
{
    private static readonly PropertyInfo IdProperty = typeof(Agendamento).GetProperty(nameof(Agendamento.Id))!;

    private readonly List<Agendamento> _itens = [];
    private int _proximoId = 1;

    public IReadOnlyList<Agendamento> Itens => _itens;

    /// <summary>Quantas vezes o app consultou a lista (para verificar atualizações periódicas).</summary>
    public int Consultas { get; private set; }

    public int Sincronizacoes { get; private set; }

    /// <summary>Simula o banco indisponível nas consultas.</summary>
    public bool FalharConsultas { get; set; }

    public Agendamento Semear(Agendamento agendamento)
    {
        Inserir(agendamento);
        return agendamento;
    }

    public Task<Agendamento?> ObterPorIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_itens.FirstOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<Agendamento>> ListarPorPeriodoAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        Consultas++;
        if (FalharConsultas)
            throw new InvalidOperationException("Banco indisponível.");

        IReadOnlyList<Agendamento> resultado = _itens
            .Where(a => a.Inicio < fim && a.Fim > inicio)
            .OrderBy(a => a.Inicio)
            .ToList();
        return Task.FromResult(resultado);
    }

    public Task AdicionarAsync(Agendamento agendamento, CancellationToken cancellationToken = default)
    {
        Inserir(agendamento);
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Agendamento agendamento, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task AdicionarSerieAsync(
        Agendamento original, IReadOnlyList<Agendamento> copias, CancellationToken cancellationToken = default)
    {
        foreach (var copia in copias)
            Inserir(copia);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Agendamento>> ListarPorOrigemAsync(
        OrigemAgendamento origem, DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Agendamento> resultado = _itens
            .Where(a => a.Origem == origem && a.Inicio >= inicio && a.Inicio < fim)
            .OrderBy(a => a.Inicio)
            .ThenBy(a => a.Fim)
            .ToList();
        return Task.FromResult(resultado);
    }

    /// <summary>Mesma regra do repositório real, aplicada em memória (tudo ou nada: valida antes de alterar).</summary>
    public Task<ResultadoSincronizacao> SincronizarAsync(
        OrigemAgendamento origem,
        DateTime inicio,
        DateTime fim,
        IReadOnlyList<Agendamento> itensRecebidos,
        CancellationToken cancellationToken = default)
    {
        if (origem == OrigemAgendamento.Local)
            throw new ArgumentException("A origem local não é sincronizada.", nameof(origem));
        if (itensRecebidos.Any(i => i.Origem != origem))
            throw new ArgumentException("Há itens de outra origem na lista recebida.", nameof(itensRecebidos));
        if (itensRecebidos.GroupBy(i => i.IdExterno).Any(g => g.Count() > 1))
            throw new ArgumentException("Há IdExterno repetido na lista recebida.", nameof(itensRecebidos));

        Sincronizacoes++;
        var porId = itensRecebidos.ToDictionary(i => i.IdExterno!);
        var existentes = _itens.Where(a => a.Origem == origem).ToList();

        var removidos = existentes
            .Where(a => a.Inicio >= inicio && a.Inicio < fim && !porId.ContainsKey(a.IdExterno!))
            .ToList();

        var atualizados = 0;
        var inseridos = 0;
        foreach (var recebido in itensRecebidos)
        {
            var atual = existentes.FirstOrDefault(a => a.IdExterno == recebido.IdExterno);
            if (atual is null)
            {
                Inserir(recebido);
                inseridos++;
                continue;
            }

            if (Mudou(atual, recebido))
                atualizados++;

            atual.AtualizarDaOrigem(
                recebido.UrlExterna, recebido.Titulo, recebido.Descricao,
                recebido.Inicio, recebido.Fim, recebido.DiaInteiro, recebido.Status);
        }

        foreach (var removido in removidos)
            _itens.Remove(removido);

        return Task.FromResult(new ResultadoSincronizacao(inseridos, atualizados, removidos.Count));
    }

    private static bool Mudou(Agendamento atual, Agendamento recebido) =>
        atual.UrlExterna != recebido.UrlExterna
        || atual.Titulo != recebido.Titulo
        || atual.Descricao != recebido.Descricao
        || atual.Inicio != recebido.Inicio
        || atual.Fim != recebido.Fim
        || atual.DiaInteiro != recebido.DiaInteiro
        || atual.Status != recebido.Status;

    private void Inserir(Agendamento agendamento)
    {
        // Espelha o índice único filtrado (Origem, IdExterno) do banco.
        if (agendamento.IdExterno is not null
            && _itens.Any(a => a.Origem == agendamento.Origem && a.IdExterno == agendamento.IdExterno))
            throw new InvalidOperationException($"IdExterno duplicado: {agendamento.IdExterno}");

        IdProperty.SetValue(agendamento, _proximoId++);
        _itens.Add(agendamento);
    }
}

/// <summary>Registra as notificações pedidas; pode ser configurado para falhar.</summary>
public sealed class FakeNotificador : INotificador
{
    public List<(AgendamentoDto Agendamento, int MinutosRestantes)> Notificacoes { get; } = [];

    public int Tentativas { get; private set; }

    /// <summary>Quantas tentativas seguintes devem lançar exceção.</summary>
    public int FalhasPendentes { get; set; }

    public IEnumerable<string> Titulos => Notificacoes.Select(n => n.Agendamento.Titulo);

    public void Notificar(AgendamentoDto agendamento, int minutosRestantes)
    {
        Tentativas++;
        if (FalhasPendentes > 0)
        {
            FalhasPendentes--;
            throw new InvalidOperationException("Falha ao exibir o toast.");
        }

        Notificacoes.Add((agendamento, minutosRestantes));
    }
}

public sealed class FakePreferenciasService : IPreferenciasService
{
    public Preferencias Atual { get; private set; } = new();

    public event Action? Alterado;

    public void Salvar(Preferencias preferencias)
    {
        Atual = preferencias;
        Alterado?.Invoke();
    }
}

/// <summary>Serviço de sincronização falso: conta chamadas e dispara os eventos sob demanda.</summary>
public sealed class SincronizacaoFalsa : DateCheck.Application.Servicos.ISincronizacaoExternaService
{
    private int _chamadas;
    private int _chamadasAguardadas;
    private readonly SemaphoreSlim _sinal = new(0);

    public int Chamadas => Volatile.Read(ref _chamadas);

    /// <summary>
    /// Espera, SEM depender de tempo real, até que a chamada de número <paramref name="total"/> tenha acontecido.
    /// O serviço sinaliza cada chamada; o limite só existe para o teste falhar em vez de travar se houver um bug.
    /// </summary>
    public async Task AguardarChamadasAsync(int total, TimeSpan? limite = null)
    {
        var espera = limite ?? TimeSpan.FromSeconds(30);
        while (_chamadasAguardadas < total)
        {
            if (!await _sinal.WaitAsync(espera))
                throw new TimeoutException($"Esperava {total} chamada(s) de sincronização, mas só houve {Chamadas}.");

            _chamadasAguardadas++;
        }
    }

    public List<bool> Manuais { get; } = [];

    public Exception? ExcecaoNaProxima { get; set; }

    public DateCheck.Application.Servicos.EstadoDaSincronizacao Estado { get; set; } =
        DateCheck.Application.Servicos.EstadoDaSincronizacao.NaoConfigurado;

    public event Action<DateCheck.Application.Servicos.EstadoDaSincronizacao>? EstadoAlterado;

    public event Action? DadosAlterados;

    public event Action? ReconexaoNecessaria;

    public void DispararDadosAlterados() => DadosAlterados?.Invoke();

    public void DispararReconexao() => ReconexaoNecessaria?.Invoke();

    public void DispararEstado(DateCheck.Application.Servicos.EstadoDaSincronizacao estado)
    {
        Estado = estado;
        EstadoAlterado?.Invoke(estado);
    }

    public Task<DateCheck.Application.Servicos.EstadoDaSincronizacao> SincronizarAsync(
        bool manual = false, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _chamadas);
        lock (Manuais)
            Manuais.Add(manual);
        _sinal.Release(); // antes de qualquer exceção: a chamada aconteceu, falhando ou não

        if (ExcecaoNaProxima is { } excecao)
        {
            ExcecaoNaProxima = null;
            throw excecao;
        }

        return Task.FromResult(Estado);
    }

    public int Reinicios { get; private set; }

    public void Reiniciar() => Reinicios++;

    public Task<TResultado> ExecutarSemSincronizacaoAsync<TResultado>(
        Func<Task<TResultado>> acao, CancellationToken cancellationToken = default) => acao();
}

/// <summary>Serviço do Notion falso para a aba das Configurações.</summary>
public sealed class NotionServiceFalso : DateCheck.Application.Notion.INotionService
{
    public bool EstaConectado { get; set; }

    public DateCheck.Application.Notion.ConfiguracaoNotion? ConfiguracaoAtual { get; set; }

    public DateCheck.Application.Notion.EstruturaDoBancoNotion? Estrutura { get; set; }

    public Exception? Falha { get; set; }

    public string ResultadoDoTeste { get; set; } = "Conectado como DateCheck (Casa). Banco \"Tarefas\" acessível.";

    public int Removidos { get; set; }

    public List<(string Token, string Link)> Inspecoes { get; } = [];

    public List<(string Token, string DataSourceId, string PropriedadeData, DateCheck.Application.Notion.ConclusaoNotion? Conclusao)> Conexoes { get; } = [];

    public int Testes { get; private set; }

    public int Desconexoes { get; private set; }

    public Task<DateCheck.Application.Notion.EstruturaDoBancoNotion> InspecionarAsync(
        string token, string linkOuIdDoBanco, CancellationToken cancellationToken = default)
    {
        Inspecoes.Add((token, linkOuIdDoBanco));
        if (Falha is not null)
            throw Falha;
        return Task.FromResult(Estrutura ?? throw new InvalidOperationException("Estrutura não configurada"));
    }

    public Task ConectarAsync(
        string token,
        DateCheck.Application.Notion.EstruturaDoBancoNotion estrutura,
        string dataSourceId,
        string propriedadeData,
        DateCheck.Application.Notion.ConclusaoNotion? conclusao,
        CancellationToken cancellationToken = default)
    {
        if (Falha is not null)
            throw Falha;

        Conexoes.Add((token, dataSourceId, propriedadeData, conclusao));
        EstaConectado = true;
        ConfiguracaoAtual = new DateCheck.Application.Notion.ConfiguracaoNotion(estrutura.DatabaseId, dataSourceId, propriedadeData, conclusao);
        return Task.CompletedTask;
    }

    public Task<string> TestarConexaoAsync(CancellationToken cancellationToken = default)
    {
        Testes++;
        if (Falha is not null)
            throw Falha;
        return Task.FromResult(ResultadoDoTeste);
    }

    public Task<int> DesconectarAsync(CancellationToken cancellationToken = default)
    {
        Desconexoes++;
        if (Falha is not null)
            throw Falha;
        EstaConectado = false;
        ConfiguracaoAtual = null;
        return Task.FromResult(Removidos);
    }
}
