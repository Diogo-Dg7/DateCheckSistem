using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DateCheck.Application.Servicos;
using DateCheck.Desktop.Navegacao;
using DateCheck.Desktop.Servicos;

namespace DateCheck.Desktop.ViewModels;

/// <summary>Compromissos de um dia (por padrão, hoje), com destaque para o próximo e para os atrasados.</summary>
public sealed partial class HojeViewModel : ViewModelBase, ITela, IDisposable
{
    // O "próximo" e os "atrasados" mudam com o passar do tempo, mesmo sem ação do usuário.
    internal static readonly TimeSpan IntervaloDeAtualizacao = TimeSpan.FromSeconds(30);

    private readonly IAgendamentoService _agendamentos;
    private readonly IConflitoService _conflitos;
    private readonly IDuplicacaoService _duplicacao;
    private readonly INavegacao _navegacao;
    private readonly IAbridorDeLinks _abridorDeLinks;
    private readonly TimeProvider _relogio;

    private ITimer? _timer;
    private SynchronizationContext? _contexto;
    private bool _acompanhaHoje = true;

    public HojeViewModel(
        IAgendamentoService agendamentos,
        IConflitoService conflitos,
        IDuplicacaoService duplicacao,
        INavegacao navegacao,
        IAbridorDeLinks abridorDeLinks,
        IDialogService dialogos,
        TimeProvider relogio) : base(dialogos)
    {
        _agendamentos = agendamentos;
        _conflitos = conflitos;
        _duplicacao = duplicacao;
        _navegacao = navegacao;
        _abridorDeLinks = abridorDeLinks;
        _relogio = relogio;
        _dia = Hoje;
    }

    public ObservableCollection<AgendamentoItemViewModel> Itens { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo), nameof(Subtitulo), nameof(EhHoje))]
    private DateTime _dia;

    [ObservableProperty]
    private bool _estaVazio;

    public bool EhHoje => Dia.Date == Hoje;

    public string Titulo => EhHoje ? "Hoje" : Capitalizar(Dia.ToString("dddd", CultureInfo.CurrentCulture));

    public string Subtitulo => Dia.ToString("D", CultureInfo.CurrentCulture);

    private DateTime Hoje => _relogio.GetLocalNow().DateTime.Date;

    /// <summary>Define o dia exibido; nulo (ou o dia de hoje) faz a tela acompanhar o relógio.</summary>
    public void DefinirDia(DateTime? dia)
    {
        var escolhido = dia?.Date ?? Hoje;
        _acompanhaHoje = escolhido == Hoje;
        Dia = escolhido;
    }

    public async Task AtivarAsync()
    {
        _contexto = SynchronizationContext.Current;
        _timer?.Dispose();
        _timer = _relogio.CreateTimer(
            _ => Postar(() => _ = RecarregarSemAlardeAsync()),
            null,
            IntervaloDeAtualizacao,
            IntervaloDeAtualizacao);

        await RecarregarAsync();
    }

    public void Desativar()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Desativar();

    public async Task RecarregarAsync()
    {
        if (_acompanhaHoje)
            Dia = Hoje;

        var lista = await _agendamentos.ListarDoDiaAsync(Dia);

        // Só mexe na coleção se algo mudou, para não perder a rolagem a cada atualização periódica.
        if (!lista.SequenceEqual(Itens.Select(i => i.Dto)))
        {
            Itens.Clear();
            foreach (var dto in lista)
                Itens.Add(new AgendamentoItemViewModel(dto));
        }

        EstaVazio = Itens.Count == 0;
        OnPropertyChanged(nameof(EhHoje));
        OnPropertyChanged(nameof(Titulo));
    }

    [RelayCommand]
    private Task DiaAnteriorAsync() => MudarDiaAsync(Dia.AddDays(-1));

    [RelayCommand]
    private Task DiaSeguinteAsync() => MudarDiaAsync(Dia.AddDays(1));

    [RelayCommand]
    private Task VoltarParaHojeAsync() => MudarDiaAsync(Hoje);

    [RelayCommand]
    private Task IrParaCalendarioAsync() => _navegacao.MostrarCalendarioAsync();

    [RelayCommand]
    private Task ConcluirAsync(AgendamentoItemViewModel? item) =>
        item is null ? Task.CompletedTask : AlterarEAtualizarAsync(() => _agendamentos.ConcluirAsync(item.Id));

    [RelayCommand]
    private async Task CancelarAsync(AgendamentoItemViewModel? item)
    {
        if (item is null || !Dialogos.Confirmar("Cancelar compromisso", $"Cancelar \"{item.Titulo}\"?"))
            return;

        await AlterarEAtualizarAsync(() => _agendamentos.CancelarAsync(item.Id));
    }

    [RelayCommand]
    private Task ReabrirAsync(AgendamentoItemViewModel? item) =>
        item is null ? Task.CompletedTask : AlterarEAtualizarAsync(() => _agendamentos.ReabrirAsync(item.Id));

    [RelayCommand]
    private async Task NovoAsync()
    {
        if (Dialogos.AbrirFormulario(null, Dia))
            await RecarregarAsync();
    }

    [RelayCommand]
    private async Task EditarAsync(AgendamentoItemViewModel? item)
    {
        if (item is not null && Dialogos.AbrirFormulario(item.Id))
            await RecarregarAsync();
    }

    /// <summary>Copia o compromisso para outra data/hora, mantendo título, descrição e duração.</summary>
    [RelayCommand]
    private async Task DuplicarAsync(AgendamentoItemViewModel? item)
    {
        if (item is null)
            return;

        var novoInicio = Dialogos.PedirDataHora(
            "Duplicar compromisso",
            $"Nova data e hora de início para a cópia de \"{item.Titulo}\":",
            item.Dto.Inicio.AddDays(1));
        if (novoInicio is not { } inicio)
            return;

        var duplicou = false;
        await ExecutarAsync(async () =>
        {
            var duracao = item.Dto.Fim - item.Dto.Inicio;
            var conflitos = await _conflitos.BuscarConflitosAsync(inicio, inicio + duracao);
            if (conflitos.Count > 0 && !Dialogos.Confirmar("Conflito de horários", MensagemDeConflitos.Montar(conflitos)))
                return;

            await _duplicacao.DuplicarAsync(item.Id, inicio);
            duplicou = true;
        });

        // Mostra o dia onde a cópia caiu, para o usuário vê-la.
        if (duplicou && inicio.Date != Dia)
            DefinirDia(inicio);

        await RecarregarAsync();
    }

    /// <summary>Abre o item externo (ex.: página do Notion) no navegador padrão.</summary>
    [RelayCommand]
    private void AbrirNaOrigem(AgendamentoItemViewModel? item)
    {
        if (item is not { PodeAbrirNaOrigem: true })
            return;

        if (!_abridorDeLinks.Abrir(item.Dto.UrlExterna!))
            Dialogos.MostrarErro("Não foi possível abrir o link no navegador.");
    }

    private Task MudarDiaAsync(DateTime dia)
    {
        DefinirDia(dia);
        return RecarregarAsync();
    }

    /// <summary>Executa a ação (mostrando erros de regra de negócio) e atualiza a lista de qualquer forma.</summary>
    private async Task AlterarEAtualizarAsync(Func<Task> acao)
    {
        await ExecutarAsync(acao);
        await RecarregarAsync();
    }

    /// <summary>Falhas na atualização periódica são ignoradas: a próxima tentativa corrige.</summary>
    private async Task RecarregarSemAlardeAsync()
    {
        try
        {
            await RecarregarAsync();
        }
        catch (Exception)
        {
            // Mantém a lista atual na tela.
        }
    }

    private void Postar(Action acao)
    {
        if (_contexto is null)
            acao();
        else
            _contexto.Post(_ => acao(), null);
    }

    private static string Capitalizar(string texto) =>
        texto.Length == 0 ? texto : char.ToUpper(texto[0], CultureInfo.CurrentCulture) + texto[1..];
}
