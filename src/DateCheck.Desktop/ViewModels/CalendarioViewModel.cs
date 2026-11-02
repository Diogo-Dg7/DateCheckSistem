using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DateCheck.Application.Servicos;
using DateCheck.Desktop.Navegacao;
using DateCheck.Desktop.Servicos;

namespace DateCheck.Desktop.ViewModels;

/// <summary>Grade mensal (6 semanas, começando no domingo) com os compromissos de cada dia.</summary>
public sealed partial class CalendarioViewModel : ViewModelBase, ITela
{
    public static IReadOnlyList<string> DiasDaSemana { get; } = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];

    private readonly IAgendamentoService _agendamentos;
    private readonly INavegacao _navegacao;
    private readonly TimeProvider _relogio;

    private bool _jaAtivado;

    public CalendarioViewModel(
        IAgendamentoService agendamentos,
        INavegacao navegacao,
        IDialogService dialogos,
        TimeProvider relogio) : base(dialogos)
    {
        _agendamentos = agendamentos;
        _navegacao = navegacao;
        _relogio = relogio;
        _mes = PrimeiroDiaDoMes(Hoje);
    }

    public ObservableCollection<DiaCalendarioViewModel> Dias { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private DateTime _mes;

    public string Titulo
    {
        get
        {
            var texto = Mes.ToString("MMMM 'de' yyyy", CultureInfo.CurrentCulture);
            return char.ToUpper(texto[0], CultureInfo.CurrentCulture) + texto[1..];
        }
    }

    private DateTime Hoje => _relogio.GetLocalNow().DateTime.Date;

    public async Task AtivarAsync()
    {
        // Na primeira vez abre no mês atual; depois mantém o mês que o usuário estava vendo.
        if (!_jaAtivado)
        {
            Mes = PrimeiroDiaDoMes(Hoje);
            _jaAtivado = true;
        }

        await CarregarAsync();
    }

    public void Desativar()
    {
    }

    public async Task CarregarAsync()
    {
        var grade = GradeDoMes.Dias(Mes.Year, Mes.Month);

        var agendamentos = await _agendamentos.ListarPeriodoAsync(grade[0], grade[^1].AddDays(1));

        var dias = GradeDoMes.Montar(Mes.Year, Mes.Month, Hoje, agendamentos);

        Dias.Clear();
        foreach (var dia in dias)
            Dias.Add(dia);
    }

    [RelayCommand]
    private Task MesAnteriorAsync() => MudarMesAsync(Mes.AddMonths(-1));

    [RelayCommand]
    private Task MesSeguinteAsync() => MudarMesAsync(Mes.AddMonths(1));

    [RelayCommand]
    private Task IrParaMesAtualAsync() => MudarMesAsync(PrimeiroDiaDoMes(Hoje));

    [RelayCommand]
    private Task IrParaHojeAsync() => _navegacao.MostrarHojeAsync();

    /// <summary>Clicar num dia abre a tela Hoje mostrando os compromissos dele.</summary>
    [RelayCommand]
    private Task AbrirDiaAsync(DiaCalendarioViewModel? dia) =>
        dia is null ? Task.CompletedTask : _navegacao.MostrarHojeAsync(dia.Data);

    private Task MudarMesAsync(DateTime mes)
    {
        Mes = PrimeiroDiaDoMes(mes);
        return CarregarAsync();
    }

    private static DateTime PrimeiroDiaDoMes(DateTime data) => new(data.Year, data.Month, 1);
}
