using DateCheck.Application.Dtos;

namespace DateCheck.Desktop.ViewModels;

/// <summary>Monta a grade do calendário mensal: 6 semanas começando no domingo.</summary>
public static class GradeDoMes
{
    public const int Semanas = 6;
    public const int TotalDeDias = Semanas * 7;

    /// <summary>Quantos compromissos aparecem em cada célula; o restante vira "+N mais".</summary>
    public const int MaximoPorDia = 3;

    /// <summary>Os 42 dias da grade do mês: do domingo anterior (ou do dia 1, se for domingo) em diante.</summary>
    public static IReadOnlyList<DateTime> Dias(int ano, int mes)
    {
        var primeiroDoMes = new DateTime(ano, mes, 1);
        var inicio = primeiroDoMes.AddDays(-(int)primeiroDoMes.DayOfWeek);

        return Enumerable.Range(0, TotalDeDias).Select(i => inicio.AddDays(i)).ToList();
    }

    /// <summary>Distribui os agendamentos pelos dias da grade; um agendamento longo aparece em todos os dias que ocupa.</summary>
    public static IReadOnlyList<DiaCalendarioViewModel> Montar(
        int ano, int mes, DateTime hoje, IEnumerable<AgendamentoDto> agendamentos)
    {
        var lista = agendamentos.ToList();

        return Dias(ano, mes)
            .Select(dia =>
            {
                var doDia = lista
                    .Where(a => a.Inicio < dia.AddDays(1) && a.Fim > dia)
                    .OrderByDescending(a => a.DiaInteiro)
                    .ThenBy(a => a.Inicio)
                    .ThenBy(a => a.Fim)
                    .ToList();

                return new DiaCalendarioViewModel(dia, dia.Month == mes && dia.Year == ano, dia == hoje.Date, doDia);
            })
            .ToList();
    }
}
