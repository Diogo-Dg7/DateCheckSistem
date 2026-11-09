using DateCheck.Application.Dtos;

namespace DateCheck.Application.Servicos;

public interface IConflitoService
{
    /// <summary>
    /// Retorna os agendamentos que conflitam com o período informado. Nunca bloqueia: quem chama decide
    /// se salva mesmo assim. Cancelados, itens de dia inteiro e o agendamento <paramref name="ignorarId"/>
    /// (o próprio, ao editar) são ignorados; se o próprio período consultado é de <paramref name="diaInteiro"/>,
    /// nada conflita.
    /// </summary>
    Task<IReadOnlyList<AgendamentoDto>> BuscarConflitosAsync(
        DateTime inicio,
        DateTime fim,
        int? ignorarId = null,
        bool diaInteiro = false,
        CancellationToken cancellationToken = default);
}
