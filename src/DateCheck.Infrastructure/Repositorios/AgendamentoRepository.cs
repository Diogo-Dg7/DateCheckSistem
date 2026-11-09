using DateCheck.Application.Dtos;
using DateCheck.Application.Interfaces;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
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

    public async Task<IReadOnlyList<Agendamento>> ListarPorOrigemAsync(
        OrigemAgendamento origem, DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        await using var contexto = await _contextoFactory.CreateDbContextAsync(cancellationToken);

        return await contexto.Agendamentos
            .AsNoTracking()
            .Where(a => a.Origem == origem && a.Inicio >= inicio && a.Inicio < fim)
            .OrderBy(a => a.Inicio)
            .ThenBy(a => a.Fim)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Insere, atualiza e remove num único <c>SaveChanges</c> (transação implícita: tudo ou nada). Os itens da origem
    /// são carregados com rastreamento e atualizados por <see cref="Agendamento.AtualizarDaOrigem"/>; o EF só grava
    /// os que de fato mudaram. A remoção respeita a janela pelo <c>Inicio</c> do item local.
    /// </summary>
    public async Task<ResultadoSincronizacao> SincronizarAsync(
        OrigemAgendamento origem,
        DateTime inicio,
        DateTime fim,
        IReadOnlyList<Agendamento> itensRecebidos,
        CancellationToken cancellationToken = default)
    {
        if (origem == OrigemAgendamento.Local)
            throw new ArgumentException("A origem local não é sincronizada.", nameof(origem));
        if (itensRecebidos.Any(i => i.Origem != origem))
            throw new ArgumentException("Há itens de outra origem na lista recebida.", nameof(itensRecebidos));

        // O banco compara IdExterno sem diferenciar maiúsculas de minúsculas; o índice único também.
        var recebidosPorId = new Dictionary<string, Agendamento>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in itensRecebidos)
        {
            if (!recebidosPorId.TryAdd(item.IdExterno!, item))
                throw new ArgumentException($"IdExterno repetido na lista recebida: {item.IdExterno}", nameof(itensRecebidos));
        }

        await using var contexto = await _contextoFactory.CreateDbContextAsync(cancellationToken);

        // Todos da origem (e não só os da janela): um item recebido pode já existir com a data antiga, fora dela.
        var existentes = await contexto.Agendamentos
            .Where(a => a.Origem == origem)
            .ToListAsync(cancellationToken);
        var existentesPorId = existentes.ToDictionary(a => a.IdExterno!, StringComparer.OrdinalIgnoreCase);

        var inseridos = 0;
        foreach (var recebido in itensRecebidos)
        {
            if (existentesPorId.TryGetValue(recebido.IdExterno!, out var atual))
            {
                atual.AtualizarDaOrigem(
                    recebido.UrlExterna, recebido.Titulo, recebido.Descricao,
                    recebido.Inicio, recebido.Fim, recebido.DiaInteiro, recebido.Status);
            }
            else
            {
                contexto.Agendamentos.Add(recebido);
                inseridos++;
            }
        }

        var atualizados = contexto.ChangeTracker.Entries<Agendamento>().Count(e => e.State == EntityState.Modified);

        var removidos = existentes
            .Where(a => a.Inicio >= inicio && a.Inicio < fim && !recebidosPorId.ContainsKey(a.IdExterno!))
            .ToList();
        contexto.Agendamentos.RemoveRange(removidos);

        await contexto.SaveChangesAsync(cancellationToken);

        return new ResultadoSincronizacao(inseridos, atualizados, removidos.Count);
    }
}
