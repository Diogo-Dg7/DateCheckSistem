using DateCheck.Application.Dtos;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Interfaces;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Application.Servicos;

public sealed class AgendamentoService : IAgendamentoService
{
    private readonly IAgendamentoRepository _repositorio;
    private readonly TimeProvider _relogio;

    public AgendamentoService(IAgendamentoRepository repositorio, TimeProvider relogio)
    {
        _repositorio = repositorio;
        _relogio = relogio;
    }

    public async Task<AgendamentoDto> ObterAsync(int id, CancellationToken cancellationToken = default)
    {
        var agendamento = await ObterOuFalharAsync(id, cancellationToken);
        return AgendamentoMapper.ParaDto(agendamento, Agora());
    }

    public async Task<AgendamentoDto> CriarAsync(
        NovoAgendamentoRequest request, CancellationToken cancellationToken = default)
    {
        var agendamento = new Agendamento(request.Titulo, request.Inicio, request.Fim, request.Descricao);

        await _repositorio.AdicionarAsync(agendamento, cancellationToken);

        return AgendamentoMapper.ParaDto(agendamento, Agora());
    }

    public async Task<AgendamentoDto> EditarAsync(
        EditarAgendamentoRequest request, CancellationToken cancellationToken = default)
    {
        var agendamento = await ObterOuFalharAsync(request.Id, cancellationToken);

        agendamento.AlterarDetalhes(request.Titulo, request.Descricao);
        agendamento.Reagendar(request.Inicio, request.Fim);

        await _repositorio.AtualizarAsync(agendamento, cancellationToken);

        return AgendamentoMapper.ParaDto(agendamento, Agora());
    }

    public Task<AgendamentoDto> ConcluirAsync(int id, CancellationToken cancellationToken = default) =>
        AlterarStatusAsync(id, a => a.Concluir(), cancellationToken);

    public Task<AgendamentoDto> CancelarAsync(int id, CancellationToken cancellationToken = default) =>
        AlterarStatusAsync(id, a => a.Cancelar(), cancellationToken);

    public Task<AgendamentoDto> ReabrirAsync(int id, CancellationToken cancellationToken = default) =>
        AlterarStatusAsync(id, a => a.Reabrir(), cancellationToken);

    public async Task<IReadOnlyList<AgendamentoDto>> ListarDoDiaAsync(
        DateTime? dia = null, CancellationToken cancellationToken = default)
    {
        var agora = Agora();
        var data = (dia ?? agora).Date;

        var agendamentos = await _repositorio.ListarPorPeriodoAsync(data, data.AddDays(1), cancellationToken);

        var proximoId = data == agora.Date
            ? agendamentos
                .Where(a => a.Status == StatusAgendamento.Pendente && !a.DiaInteiro && a.Fim > agora)
                .OrderBy(a => a.Inicio)
                .Select(a => (int?)a.Id)
                .FirstOrDefault()
            : null;

        return Ordenar(agendamentos)
            .Select(a => AgendamentoMapper.ParaDto(a, agora, proximo: a.Id == proximoId))
            .ToList();
    }

    public async Task<IReadOnlyList<AgendamentoDto>> ListarPeriodoAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        var agendamentos = await _repositorio.ListarPorPeriodoAsync(inicio, fim, cancellationToken);

        var agora = Agora();
        return Ordenar(agendamentos)
            .Select(a => AgendamentoMapper.ParaDto(a, agora))
            .ToList();
    }

    public Task<IReadOnlyList<AgendamentoDto>> ListarDoMesAsync(
        int ano, int mes, CancellationToken cancellationToken = default)
    {
        var primeiroDia = new DateTime(ano, mes, 1);
        return ListarPeriodoAsync(primeiroDia, primeiroDia.AddMonths(1), cancellationToken);
    }

    private async Task<AgendamentoDto> AlterarStatusAsync(
        int id, Action<Agendamento> acao, CancellationToken cancellationToken)
    {
        var agendamento = await ObterOuFalharAsync(id, cancellationToken);

        acao(agendamento);

        await _repositorio.AtualizarAsync(agendamento, cancellationToken);

        return AgendamentoMapper.ParaDto(agendamento, Agora());
    }

    private async Task<Agendamento> ObterOuFalharAsync(int id, CancellationToken cancellationToken) =>
        await _repositorio.ObterPorIdAsync(id, cancellationToken)
        ?? throw new AgendamentoNaoEncontradoException(id);

    /// <summary>Dia inteiro primeiro, depois por horário.</summary>
    private static IEnumerable<Agendamento> Ordenar(IEnumerable<Agendamento> agendamentos) =>
        agendamentos
            .OrderByDescending(a => a.DiaInteiro)
            .ThenBy(a => a.Inicio)
            .ThenBy(a => a.Fim);

    private DateTime Agora() => _relogio.GetLocalNow().DateTime;
}
