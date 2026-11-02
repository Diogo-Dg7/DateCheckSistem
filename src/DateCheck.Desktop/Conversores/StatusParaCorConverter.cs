using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DateCheck.Domain.Enums;

namespace DateCheck.Desktop.Conversores;

/// <summary>Cor de destaque de cada status (barra do cartão e chips do calendário).</summary>
public sealed class StatusParaCorConverter : IValueConverter
{
    private static readonly Brush Pendente = Congelar("#2563EB");
    private static readonly Brush Concluido = Congelar("#16A34A");
    private static readonly Brush Cancelado = Congelar("#9CA3AF");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            StatusAgendamento.Concluido => Concluido,
            StatusAgendamento.Cancelado => Cancelado,
            _ => Pendente
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Congelar(string cor)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cor));
        brush.Freeze();
        return brush;
    }
}
