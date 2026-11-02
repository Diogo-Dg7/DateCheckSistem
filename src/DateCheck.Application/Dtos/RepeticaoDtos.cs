namespace DateCheck.Application.Dtos;

public enum FrequenciaRepeticao
{
    Diaria = 1,
    Semanal = 2,
    Mensal = 3
}

/// <param name="DataFim">Obrigatória e inclusiva: última data em que uma cópia pode começar (máximo 1 ano após o original).</param>
/// <param name="Intervalo">"A cada N" dias, semanas ou meses.</param>
public sealed record RepetirRequest(
    int AgendamentoId,
    FrequenciaRepeticao Frequencia,
    DateTime DataFim,
    int Intervalo = 1);

public sealed record CopiaGerada(AgendamentoDto Agendamento, IReadOnlyList<AgendamentoDto> Conflitos);

/// <summary>Cópias geradas (sem o original) e, para cada uma, os agendamentos existentes com que conflita.</summary>
public sealed record ResultadoRepeticao(Guid SerieId, IReadOnlyList<CopiaGerada> Copias)
{
    public int TotalComConflito => Copias.Count(c => c.Conflitos.Count > 0);
}
