using System.Reflection;
using DateCheck.Application.Interfaces;
using DateCheck.Domain.Entidades;

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

    /// <summary>Insere direto no "banco" sem contar como gravação do serviço testado.</summary>
    public Agendamento Semear(Agendamento agendamento)
    {
        Inserir(agendamento);
        return agendamento;
    }

    private void Inserir(Agendamento agendamento)
    {
        IdProperty.SetValue(agendamento, _proximoId++);
        _itens.Add(agendamento);
    }
}
