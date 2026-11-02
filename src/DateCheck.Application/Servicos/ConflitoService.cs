using DateCheck.Application.Dtos;
using DateCheck.Application.Interfaces;
using DateCheck.Domain.Enums;

namespace DateCheck.Application.Servicos;

public sealed class ConflitoService : IConflitoService
{
    private readonly IAgendamentoRepository _repositorio;

    public ConflitoService(IAgendamentoRepository repositorio)
    {
        _repositorio = repositorio;
    }

    public async Task<IReadOnlyList<AgendamentoDto>> BuscarConflitosAsync(
        DateTime inicio,
        DateTime fim,
        int? ignorarId = null,
        CancellationToken cancellationToken = default)
    {
        var candidatos = await _repositorio.ListarPorPeriodoAsync(inicio, fim, cancellationToken);

        return candidatos
            .Where(a => a.Status != StatusAgendamento.Cancelado)
            .Where(a => a.Id != ignorarId)
            .Where(a => Conflitam(inicio, fim, a.Inicio, a.Fim))
            .OrderBy(a => a.Inicio)
            .Select(a => AgendamentoMapper.ParaDto(a))
            .ToList();
    }

    /// <summary>Intervalos que apenas se encostam (fim de um = início do outro) não conflitam.</summary>
    internal static bool Conflitam(DateTime inicioA, DateTime fimA, DateTime inicioB, DateTime fimB) =>
        inicioA < fimB && inicioB < fimA;
}
