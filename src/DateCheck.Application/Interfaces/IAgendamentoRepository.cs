using DateCheck.Domain.Entidades;

namespace DateCheck.Application.Interfaces;

public interface IAgendamentoRepository
{
    Task<Agendamento?> ObterPorIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista os agendamentos que se sobrepõem ao período informado
    /// (<c>Inicio &lt; fim &amp;&amp; Fim &gt; inicio</c>), em ordem de início.
    /// </summary>
    Task<IReadOnlyList<Agendamento>> ListarPorPeriodoAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    Task AdicionarAsync(Agendamento agendamento, CancellationToken cancellationToken = default);

    Task AtualizarAsync(Agendamento agendamento, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atualiza o agendamento original e insere as cópias na mesma operação (tudo ou nada).
    /// </summary>
    Task AdicionarSerieAsync(
        Agendamento original,
        IReadOnlyList<Agendamento> copias,
        CancellationToken cancellationToken = default);
}
