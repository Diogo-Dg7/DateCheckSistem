using DateCheck.Application.Dtos;

namespace DateCheck.Application.Servicos;

public interface IAgendamentoService
{
    Task<AgendamentoDto> ObterAsync(int id, CancellationToken cancellationToken = default);

    Task<AgendamentoDto> CriarAsync(NovoAgendamentoRequest request, CancellationToken cancellationToken = default);

    Task<AgendamentoDto> EditarAsync(EditarAgendamentoRequest request, CancellationToken cancellationToken = default);

    Task<AgendamentoDto> ConcluirAsync(int id, CancellationToken cancellationToken = default);

    Task<AgendamentoDto> CancelarAsync(int id, CancellationToken cancellationToken = default);

    Task<AgendamentoDto> ReabrirAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Agendamentos do dia em ordem de horário (hoje, se <paramref name="dia"/> for nulo). Quando o dia é hoje,
    /// marca o próximo compromisso; pendentes que já terminaram vêm marcados como atrasados.
    /// </summary>
    Task<IReadOnlyList<AgendamentoDto>> ListarDoDiaAsync(
        DateTime? dia = null, CancellationToken cancellationToken = default);

    /// <summary>Agendamentos que se sobrepõem a [<paramref name="inicio"/>, <paramref name="fim"/>), em ordem de início.</summary>
    Task<IReadOnlyList<AgendamentoDto>> ListarPeriodoAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    /// <summary>Agendamentos que tocam o mês informado, em ordem de início.</summary>
    Task<IReadOnlyList<AgendamentoDto>> ListarDoMesAsync(
        int ano, int mes, CancellationToken cancellationToken = default);
}
