using DateCheck.Domain.Enums;

namespace DateCheck.Application.Dtos;

/// <param name="Atrasado">Pendente cujo horário de término já passou.</param>
/// <param name="Proximo">Pendente de hoje que ainda não terminou e é o primeiro da fila.</param>
public sealed record AgendamentoDto(
    int Id,
    string Titulo,
    string? Descricao,
    DateTime Inicio,
    DateTime Fim,
    StatusAgendamento Status,
    Guid? SerieId,
    bool Atrasado = false,
    bool Proximo = false);
