using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DateCheck.Application.Dtos;
using DateCheck.Application.Servicos;
using DateCheck.Desktop.Servicos;

namespace DateCheck.Desktop.ViewModels;

public sealed record OpcaoFrequencia(FrequenciaRepeticao Valor, string Texto);

/// <summary>
/// Criar/editar um compromisso. Fluxo de salvar: avisa conflitos (sem bloquear), grava e, se pedido,
/// mostra a prévia da repetição antes de gerar as cópias.
/// </summary>
public sealed partial class AgendamentoFormViewModel : ViewModelBase
{
    private static readonly string[] FormatosDeHora = ["HH:mm", "H:mm"];

    private readonly IAgendamentoService _agendamentos;
    private readonly IConflitoService _conflitos;
    private readonly IRepeticaoService _repeticao;
    private readonly TimeProvider _relogio;

    private int? _id;
    private DateTime? _diaSugerido;

    public AgendamentoFormViewModel(
        IAgendamentoService agendamentos,
        IConflitoService conflitos,
        IRepeticaoService repeticao,
        IDialogService dialogos,
        TimeProvider relogio) : base(dialogos)
    {
        _agendamentos = agendamentos;
        _conflitos = conflitos;
        _repeticao = repeticao;
        _relogio = relogio;
    }

    public static IReadOnlyList<OpcaoFrequencia> Frequencias { get; } =
    [
        new(FrequenciaRepeticao.Diaria, "Diariamente"),
        new(FrequenciaRepeticao.Semanal, "Semanalmente"),
        new(FrequenciaRepeticao.Mensal, "Mensalmente")
    ];

    /// <summary>Pedido para fechar a janela; o argumento indica se algo foi salvo.</summary>
    public event Action<bool>? FechamentoSolicitado;

    public bool EhEdicao => _id is not null;

    public string TituloDaJanela => EhEdicao ? "Editar compromisso" : "Novo compromisso";

    [ObservableProperty]
    private string _titulo = string.Empty;

    [ObservableProperty]
    private string _descricao = string.Empty;

    [ObservableProperty]
    private DateTime? _dataInicio;

    [ObservableProperty]
    private string _horaInicio = string.Empty;

    [ObservableProperty]
    private DateTime? _dataFim;

    [ObservableProperty]
    private string _horaFim = string.Empty;

    [ObservableProperty]
    private bool _repetir;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnidadeDoIntervalo))]
    private FrequenciaRepeticao _frequencia = FrequenciaRepeticao.Semanal;

    [ObservableProperty]
    private int _intervalo = 1;

    [ObservableProperty]
    private DateTime? _repetirAte;

    public string UnidadeDoIntervalo => Frequencia switch
    {
        FrequenciaRepeticao.Diaria => "dia(s)",
        FrequenciaRepeticao.Semanal => "semana(s)",
        _ => "mês(es)"
    };

    /// <summary>Define o que será editado/criado; chamado antes de a janela abrir.</summary>
    public void Preparar(int? agendamentoId, DateTime? diaSugerido)
    {
        _id = agendamentoId;
        _diaSugerido = diaSugerido;
        OnPropertyChanged(nameof(EhEdicao));
        OnPropertyChanged(nameof(TituloDaJanela));
    }

    public async Task CarregarAsync()
    {
        if (_id is { } id)
        {
            var dto = await _agendamentos.ObterAsync(id);
            Titulo = dto.Titulo;
            Descricao = dto.Descricao ?? string.Empty;
            PreencherPeriodo(dto.Inicio, dto.Fim);
            return;
        }

        var agora = _relogio.GetLocalNow().DateTime;
        var dia = (_diaSugerido ?? agora).Date;

        // Hoje: próxima hora cheia. Outro dia: 09:00.
        var inicio = dia == agora.Date
            ? agora.Date.AddHours(agora.Hour + 1)
            : dia.AddHours(9);
        if (inicio.Date != dia)
            inicio = dia.AddHours(23);

        PreencherPeriodo(inicio, inicio.AddHours(1));
    }

    [RelayCommand]
    private async Task SalvarAsync()
    {
        if (!TentarMontarPeriodo(out var inicio, out var fim))
            return;

        if (Repetir && RepetirAte is null)
        {
            Dialogos.MostrarAviso("Informe até quando o compromisso deve se repetir.");
            return;
        }

        var salvou = false;
        AgendamentoDto? salvo = null;

        // 1) Conflitos avisam, mas não bloqueiam.
        var seguir = true;
        var ok = await ExecutarAsync(async () =>
        {
            var conflitos = await _conflitos.BuscarConflitosAsync(inicio, fim, ignorarId: _id);
            if (conflitos.Count > 0)
                seguir = Dialogos.Confirmar("Conflito de horários", MensagemDeConflitos.Montar(conflitos));
        });
        if (!ok || !seguir)
            return;

        // 2) Grava o compromisso.
        ok = await ExecutarAsync(async () =>
        {
            salvo = _id is { } id
                ? await _agendamentos.EditarAsync(new EditarAgendamentoRequest(id, Titulo, inicio, fim, Descricao))
                : await _agendamentos.CriarAsync(new NovoAgendamentoRequest(Titulo, inicio, fim, Descricao));
            salvou = true;
        });
        if (!ok)
            return;

        // 3) Repetição: prévia com conflitos, confirmação e só então gravação das cópias.
        if (Repetir && salvo is not null)
            await RepetirComConfirmacaoAsync(salvo.Id);

        if (salvou)
            FechamentoSolicitado?.Invoke(true);
    }

    [RelayCommand]
    private void Cancelar() => FechamentoSolicitado?.Invoke(false);

    private async Task RepetirComConfirmacaoAsync(int agendamentoId)
    {
        var request = new RepetirRequest(agendamentoId, Frequencia, RepetirAte!.Value, Intervalo);

        await ExecutarAsync(async () =>
        {
            var previa = await _repeticao.PrevisualizarAsync(request);

            if (!Dialogos.Confirmar("Repetir compromisso", MontarMensagemDaPrevia(previa, request.DataFim)))
                return;

            await _repeticao.RepetirAsync(request);
        }, prefixoDaMensagem: "O compromisso foi salvo, mas a repetição não foi criada.");
    }

    private static string MontarMensagemDaPrevia(ResultadoRepeticao previa, DateTime ate)
    {
        var texto = $"Serão criadas {previa.Copias.Count} cópias, até {ate:dd/MM/yyyy}.";

        if (previa.TotalComConflito > 0)
        {
            var datas = previa.Copias
                .Where(c => c.Conflitos.Count > 0)
                .Select(c => c.Agendamento.Inicio.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture))
                .Take(5);

            texto += $"\n\n{previa.TotalComConflito} delas conflitam com outros compromissos: {string.Join(", ", datas)}";
            if (previa.TotalComConflito > 5)
                texto += "...";
        }

        return texto + "\n\nGerar as cópias?";
    }

    private void PreencherPeriodo(DateTime inicio, DateTime fim)
    {
        DataInicio = inicio.Date;
        HoraInicio = inicio.ToString("HH:mm", CultureInfo.InvariantCulture);
        DataFim = fim.Date;
        HoraFim = fim.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private bool TentarMontarPeriodo(out DateTime inicio, out DateTime fim)
    {
        inicio = fim = default;

        if (DataInicio is null || DataFim is null)
        {
            Dialogos.MostrarAviso("Informe a data de início e a de fim.");
            return false;
        }

        if (!TimeOnly.TryParseExact(HoraInicio.Trim(), FormatosDeHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var horaInicio)
            || !TimeOnly.TryParseExact(HoraFim.Trim(), FormatosDeHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var horaFim))
        {
            Dialogos.MostrarAviso("Informe os horários no formato HH:mm (por exemplo, 14:30).");
            return false;
        }

        inicio = DataInicio.Value.Date + horaInicio.ToTimeSpan();
        fim = DataFim.Value.Date + horaFim.ToTimeSpan();

        if (fim <= inicio)
        {
            Dialogos.MostrarAviso("O fim deve ser posterior ao início.");
            return false;
        }

        return true;
    }
}
