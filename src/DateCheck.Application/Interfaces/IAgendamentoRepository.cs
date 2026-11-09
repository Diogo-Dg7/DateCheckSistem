using DateCheck.Application.Dtos;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

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

    /// <summary>
    /// Lista os itens importados da <paramref name="origem"/> cujo <c>Inicio</c> está em
    /// [<paramref name="inicio"/>, <paramref name="fim"/>), em ordem de início. Para <see cref="OrigemAgendamento.Local"/>
    /// retorna os agendamentos locais.
    /// </summary>
    Task<IReadOnlyList<Agendamento>> ListarPorOrigemAsync(
        OrigemAgendamento origem, DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconcilia, numa única transação (tudo ou nada), os itens de uma origem externa com a lista recebida dela:
    /// <list type="bullet">
    /// <item>recebido com <c>IdExterno</c> novo → insere;</item>
    /// <item>recebido com <c>IdExterno</c> já importado → atualiza (<see cref="Agendamento.AtualizarDaOrigem"/>);</item>
    /// <item>importado cujo <c>Inicio</c> está em [<paramref name="inicio"/>, <paramref name="fim"/>) e que não foi recebido → remove.
    /// Itens fora dessa janela nunca são removidos.</item>
    /// </list>
    /// <paramref name="itensRecebidos"/> deve ser a lista COMPLETA da janela: quem chama não pode sincronizar com
    /// uma lista parcial (uma consulta que falhou no meio), senão itens válidos seriam apagados.
    /// Nunca toca em itens locais; <see cref="OrigemAgendamento.Local"/> é recusada.
    /// </summary>
    /// <exception cref="ArgumentException">Origem local, item de outra origem ou <c>IdExterno</c> repetido na lista.</exception>
    Task<ResultadoSincronizacao> SincronizarAsync(
        OrigemAgendamento origem,
        DateTime inicio,
        DateTime fim,
        IReadOnlyList<Agendamento> itensRecebidos,
        CancellationToken cancellationToken = default);
}
