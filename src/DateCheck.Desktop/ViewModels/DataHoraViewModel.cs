using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DateCheck.Desktop.Servicos;

namespace DateCheck.Desktop.ViewModels;

/// <summary>Diálogo simples que pede uma data e uma hora (usado para duplicar um compromisso).</summary>
public sealed partial class DataHoraViewModel : ViewModelBase
{
    private static readonly string[] FormatosDeHora = ["HH:mm", "H:mm"];

    public DataHoraViewModel(IDialogService dialogos, string titulo, string rotulo, DateTime inicial) : base(dialogos)
    {
        TituloDaJanela = titulo;
        Rotulo = rotulo;
        _data = inicial.Date;
        _hora = inicial.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    public string TituloDaJanela { get; }

    public string Rotulo { get; }

    [ObservableProperty]
    private DateTime? _data;

    [ObservableProperty]
    private string _hora;

    /// <summary>Valor escolhido; nulo enquanto o usuário não confirmar.</summary>
    public DateTime? Resultado { get; private set; }

    public event Action<bool>? FechamentoSolicitado;

    [RelayCommand]
    private void Confirmar()
    {
        if (Data is null
            || !TimeOnly.TryParseExact(Hora.Trim(), FormatosDeHora, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora))
        {
            Dialogos.MostrarAviso("Informe uma data e um horário no formato HH:mm (por exemplo, 14:30).");
            return;
        }

        Resultado = Data.Value.Date + hora.ToTimeSpan();
        FechamentoSolicitado?.Invoke(true);
    }

    [RelayCommand]
    private void Cancelar() => FechamentoSolicitado?.Invoke(false);
}
