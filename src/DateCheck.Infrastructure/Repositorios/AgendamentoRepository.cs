using DateCheck.Application.Interfaces;
using DateCheck.Domain.Entidades;
using DateCheck.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace DateCheck.Infrastructure.Repositorios;

/// <summary>
/// Cada operação cria e descarta o próprio contexto. Entidades lidas ficam desanexadas (sem rastreamento);
/// a gravação reanexa a entidade com <c>Update</c>.
/// </summary>
public class AgendamentoRepository : IAgendamentoRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextoFactory;

    public AgendamentoRepository(IDbContextFactory<AppDbContext> contextoFactory)
    {
        _contextoFactory = contextoFactory;
    }

    public async Task<Agendamento?> ObterPorIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var contexto = await _contextoFactory.CreateDbContextAsync(cancellationToken);

        return await contexto.Agendamentos
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Agendamento>> ListarPorPeriodoAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        await using var contexto = await _contextoFactory.CreateDbContextAsync(cancellationToken);

        return await contexto.Agendamentos
            .AsNoTracking()
            .Where(a => a.Inicio < fim && a.Fim > inicio)
            .OrderBy(a => a.Inicio)
            .ThenBy(a => a.Fim)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AdicionarAsync(Agendamento agendamento, CancellationToken cancellationToken = default)
    {
        await using var contexto = await _contextoFactory.CreateDbContextAsync(cancellationToken);

        contexto.Agendamentos.Add(agendamento);
        await contexto.SaveChangesAsync(cancellationToken);
    }

    public async Task AtualizarAsync(Agendamento agendamento, CancellationToken cancellationToken = default)
    {
        await using var contexto = await _contextoFactory.CreateDbContextAsync(cancellationToken);

        contexto.Agendamentos.Update(agendamento);
        await contexto.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Um único <c>SaveChanges</c> é uma transação implícita (tudo ou nada). Não há transação manual porque
    /// ela não funciona com a estratégia de retry do SQL Server.
    /// </summary>
    public async Task AdicionarSerieAsync(
        Agendamento original,
        IReadOnlyList<Agendamento> copias,
        CancellationToken cancellationToken = default)
    {
        await using var contexto = await _contextoFactory.CreateDbContextAsync(cancellationToken);

        contexto.Agendamentos.Update(original);
        contexto.Agendamentos.AddRange(copias);
        await contexto.SaveChangesAsync(cancellationToken);
    }
}
