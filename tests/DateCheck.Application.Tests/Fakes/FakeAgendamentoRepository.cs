using System.Reflection;
using DateCheck.Application.Dtos;
using DateCheck.Application.Interfaces;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Application.Tests.Fakes;

/// <summary>Repositório em memória para os testes dos serviços.</summary>
public sealed class FakeAgendamentoRepository : IAgendamentoRepository
{
    private static readonly PropertyInfo IdProperty =
        typeof(Agendamento).GetProperty(nameof(Agendamento.Id))!;

    private readonly List<Agendamento> _itens = [];
    private int _proximoId = 1;

    public IReadOnlyList<Agendamento> Itens => _itens;

    /// <summary>Número de chamadas de gravação, para verificar que nada foi salvo.</summary>
    public int Gravacoes { get; private set; }

    public int Sincronizacoes { get; private set; }

    public Task<Agendamento?> ObterPorIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_itens.FirstOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<Agendamento>> ListarPorPeriodoAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Agendamento> resultado = _itens
            .Where(a => a.Inicio < fim && a.Fim > inicio)
            .OrderBy(a => a.Inicio)
            .ToList();
        return Task.FromResult(resultado);
    }

    public Task AdicionarAsync(Agendamento agendamento, CancellationToken cancellationToken = default)
    {
        Inserir(agendamento);
        Gravacoes++;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Agendamento agendamento, CancellationToken cancellationToken = default)
    {
        Gravacoes++;
        return Task.CompletedTask;
    }

    public Task AdicionarSerieAsync(
        Agendamento original, IReadOnlyList<Agendamento> copias, CancellationToken cancellationToken = default)
    {
        foreach (var copia in copias)
            Inserir(copia);

        Gravacoes++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Agendamento>> ListarPorOrigemAsync(
        OrigemAgendamento origem, DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Agendamento> resultado = _itens
            .Where(a => a.Origem == origem && a.Inicio >= inicio && a.Inicio < fim)
            .OrderBy(a => a.Inicio)
            .ThenBy(a => a.Fim)
            .ToList();
        return Task.FromResult(resultado);
    }

    /// <summary>Mesma regra do repositório real, aplicada em memória (tudo ou nada: valida antes de alterar).</summary>
    public Task<ResultadoSincronizacao> SincronizarAsync(
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
        if (itensRecebidos.GroupBy(i => i.IdExterno).Any(g => g.Count() > 1))
            throw new ArgumentException("Há IdExterno repetido na lista recebida.", nameof(itensRecebidos));

        Sincronizacoes++;
        var porId = itensRecebidos.ToDictionary(i => i.IdExterno!);
        var existentes = _itens.Where(a => a.Origem == origem).ToList();

        var removidos = existentes
            .Where(a => a.Inicio >= inicio && a.Inicio < fim && !porId.ContainsKey(a.IdExterno!))
            .ToList();

        var atualizados = 0;
        var inseridos = 0;
        foreach (var recebido in itensRecebidos)
        {
            var atual = existentes.FirstOrDefault(a => a.IdExterno == recebido.IdExterno);
            if (atual is null)
            {
                Inserir(recebido);
                inseridos++;
                continue;
            }

            if (Mudou(atual, recebido))
                atualizados++;

            atual.AtualizarDaOrigem(
                recebido.UrlExterna, recebido.Titulo, recebido.Descricao,
                recebido.Inicio, recebido.Fim, recebido.DiaInteiro, recebido.Status);
        }

        foreach (var removido in removidos)
            _itens.Remove(removido);

        return Task.FromResult(new ResultadoSincronizacao(inseridos, atualizados, removidos.Count));
    }

    private static bool Mudou(Agendamento atual, Agendamento recebido) =>
        atual.UrlExterna != recebido.UrlExterna
        || atual.Titulo != recebido.Titulo
        || atual.Descricao != recebido.Descricao
        || atual.Inicio != recebido.Inicio
        || atual.Fim != recebido.Fim
        || atual.DiaInteiro != recebido.DiaInteiro
        || atual.Status != recebido.Status;

    /// <summary>Insere direto no "banco" sem contar como gravação do serviço testado.</summary>
    public Agendamento Semear(Agendamento agendamento)
    {
        Inserir(agendamento);
        return agendamento;
    }

    private void Inserir(Agendamento agendamento)
    {
        // Espelha o índice único filtrado (Origem, IdExterno) do banco.
        if (agendamento.IdExterno is not null
            && _itens.Any(a => a.Origem == agendamento.Origem && a.IdExterno == agendamento.IdExterno))
            throw new InvalidOperationException($"IdExterno duplicado: {agendamento.IdExterno}");

        IdProperty.SetValue(agendamento, _proximoId++);
        _itens.Add(agendamento);
    }
}
