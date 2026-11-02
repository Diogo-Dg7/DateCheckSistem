using DateCheck.Application.Dtos;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Interfaces;

namespace DateCheck.Application.Servicos;

public sealed class DuplicacaoService : IDuplicacaoService
{
    private readonly IAgendamentoRepository _repositorio;
    private readonly TimeProvider _relogio;

    public DuplicacaoService(IAgendamentoRepository repositorio, TimeProvider relogio)
    {
        _repositorio = repositorio;
        _relogio = relogio;
    }

    public async Task<AgendamentoDto> DuplicarAsync(
        int id, DateTime novoInicio, CancellationToken cancellationToken = default)
    {
        var original = await _repositorio.ObterPorIdAsync(id, cancellationToken)
            ?? throw new AgendamentoNaoEncontradoException(id);

        var copia = original.CopiarPara(novoInicio);

        await _repositorio.AdicionarAsync(copia, cancellationToken);

        return AgendamentoMapper.ParaDto(copia, _relogio.GetLocalNow().DateTime);
    }
}
