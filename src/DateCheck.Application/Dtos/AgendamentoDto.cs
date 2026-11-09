using DateCheck.Domain.Enums;

namespace DateCheck.Application.Dtos;

/// <param name="Atrasado">Pendente cujo horário de término já passou (nunca vale para dia inteiro).</param>
/// <param name="Proximo">Pendente de hoje que ainda não terminou e é o primeiro da fila (nunca vale para dia inteiro).</param>
/// <param name="Origem">De onde vem o item; os que não são <see cref="OrigemAgendamento.Local"/> são somente leitura.</param>
/// <param name="UrlExterna">Link do item na origem (ex.: página do Notion).</param>
/// <param name="DiaInteiro">Início e fim à meia-noite; o fim é o início do dia seguinte ao último dia.</param>
public sealed record AgendamentoDto(
    int Id,
    string Titulo,
    string? Descricao,
    DateTime Inicio,
    DateTime Fim,
    StatusAgendamento Status,
    Guid? SerieId,
    bool Atrasado = false,
    bool Proximo = false,
    OrigemAgendamento Origem = OrigemAgendamento.Local,
    string? UrlExterna = null,
    bool DiaInteiro = false)
{
    public bool EhExterno => Origem != OrigemAgendamento.Local;
}
