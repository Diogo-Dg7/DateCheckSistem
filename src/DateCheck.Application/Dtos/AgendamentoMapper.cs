using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Application.Dtos;

internal static class AgendamentoMapper
{
    public static AgendamentoDto ParaDto(Agendamento agendamento, DateTime? agora = null, bool proximo = false)
    {
        var atrasado = agora is { } hora
            && agendamento.Status == StatusAgendamento.Pendente
            && agendamento.Fim < hora;

        return new AgendamentoDto(
            agendamento.Id,
            agendamento.Titulo,
            agendamento.Descricao,
            agendamento.Inicio,
            agendamento.Fim,
            agendamento.Status,
            agendamento.SerieId,
            atrasado,
            proximo);
    }
}
