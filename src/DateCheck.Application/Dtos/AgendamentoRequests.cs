namespace DateCheck.Application.Dtos;

public sealed record NovoAgendamentoRequest(
    string Titulo,
    DateTime Inicio,
    DateTime Fim,
    string? Descricao = null);

public sealed record EditarAgendamentoRequest(
    int Id,
    string Titulo,
    DateTime Inicio,
    DateTime Fim,
    string? Descricao = null);
