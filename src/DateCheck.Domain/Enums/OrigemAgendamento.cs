namespace DateCheck.Domain.Enums;

/// <summary>De onde vem o agendamento. Itens que não são <see cref="Local"/> são somente leitura no DateCheck.</summary>
public enum OrigemAgendamento
{
    Local = 0,
    Notion = 1
}
