using DateCheck.Application.Dtos;

namespace DateCheck.Application.Servicos;

public interface IConflitoService
{
    /// <summary>
    /// Retorna os agendamentos que conflitam com o período informado. Nunca bloqueia: quem chama decide
    /// se salva mesmo assim. Cancelados e o agendamento <paramref name="ignorarId"/> (o próprio, ao editar) são ignorados.
    /// </summary>
    Task<IReadOnlyList<AgendamentoDto>> BuscarConflitosAsync(
        DateTime inicio,
        DateTime fim,
        int? ignorarId = null,
        CancellationToken cancellationToken = default);
}
