using DateCheck.Application.Dtos;
using DateCheck.Domain.Enums;

namespace DateCheck.Desktop.ViewModels;

/// <summary>Um compromisso dentro da célula de um dia (texto curto e cor pelo status).</summary>
/// <param name="Selo">Nome da origem ("Notion") nos itens externos; vazio nos locais. Aparece como tooltip do "N".</param>
public sealed record ChipCalendario(string Texto, StatusAgendamento Status, string Selo = "")
{
    public bool TemSelo => Selo.Length > 0;

    /// <summary>Só a inicial ("N"): o chip é estreito, o nome por extenso fica na tela Hoje.</summary>
    public string SeloCurto => TemSelo ? Selo[..1] : string.Empty;
}

public sealed class DiaCalendarioViewModel
{
    public DiaCalendarioViewModel(DateTime data, bool doMesAtual, bool ehHoje, IReadOnlyList<AgendamentoDto> agendamentos)
    {
        Data = data.Date;
        DoMesAtual = doMesAtual;
        EhHoje = ehHoje;
        Total = agendamentos.Count;
        Chips = agendamentos
            .Take(GradeDoMes.MaximoPorDia)
            .Select(a => new ChipCalendario(MontarTexto(a, Data), a.Status, a.EhExterno ? a.Origem.Nome() : string.Empty))
            .ToList();
    }

    public DateTime Data { get; }

    public int Numero => Data.Day;

    public bool DoMesAtual { get; }

    public bool EhHoje { get; }

    public int Total { get; }

    public IReadOnlyList<ChipCalendario> Chips { get; }

    public int Excedente => Math.Max(0, Total - Chips.Count);

    public bool TemExcedente => Excedente > 0;

    public string ExcedenteTexto => $"+{Excedente} mais";

    private static string MontarTexto(AgendamentoDto agendamento, DateTime dia) =>
        agendamento.DiaInteiro
            ? agendamento.Titulo // sem horário: a célula já diz o dia
            : agendamento.Inicio.Date == dia
                ? $"{agendamento.Inicio:HH:mm} {agendamento.Titulo}"
                : $"↳ {agendamento.Titulo}"; // continuação de um agendamento que começou antes
}
