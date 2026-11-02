using System.Reflection;
using DateCheck.Application.Dtos;
using DateCheck.Application.Interfaces;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Notificacoes;
using DateCheck.Desktop.Navegacao;
using DateCheck.Desktop.Servicos;
using DateCheck.Domain.Entidades;

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

public sealed class FakeNavegacao : INavegacao
{
    public List<DateTime?> HojeMostrado { get; } = [];

    public int CalendarioMostrado { get; private set; }

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

    private void Inserir(Agendamento agendamento)
    {
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
