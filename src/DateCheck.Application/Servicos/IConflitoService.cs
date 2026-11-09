using DateCheck.Application.Dtos;

namespace DateCheck.Application.Servicos;

public interface IConflitoService
{
    /// <summary>
    /// Retorna os agendamentos que conflitam com o período informado. Nunca bloqueia: quem chama decide
    /// se salva mesmo assim. Cancelados, itens de dia inteiro (férias, feriados) e o agendamento
    /// <paramref name="ignorarId"/> (o próprio, ao editar) são ignorados. O período consultado é sempre com horário:
    /// quando o formulário passar a criar dia inteiro, a consulta também deverá ser ignorada.
    /// </summary>
    Task<IReadOnlyList<AgendamentoDto>> BuscarConflitosAsync(
        DateTime inicio,
        DateTime fim,
        int? ignorarId = null,
        CancellationToken cancellationToken = default);
}
