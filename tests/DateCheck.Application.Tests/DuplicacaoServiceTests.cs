using DateCheck.Application.Excecoes;
using DateCheck.Application.Servicos;
using DateCheck.Application.Tests.Fakes;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Application.Tests;

public class DuplicacaoServiceTests
{
    private static readonly DateTime Inicio = new(2026, 11, 10, 9, 0, 0);
    private static readonly DateTime Fim = new(2026, 11, 10, 10, 30, 0);

    private readonly FakeAgendamentoRepository _repositorio = new();
    private readonly DuplicacaoService _servico;

    public DuplicacaoServiceTests()
    {
        _servico = new DuplicacaoService(_repositorio, TimeProvider.System);
    }

    private Agendamento Semear() => _repositorio.Semear(new Agendamento("Reunião", Inicio, Fim, "Sala 2"));

    [Fact]
    public async Task Duplicar_CriaCopiaNaNovaDataMantendoDuracao()
    {
        var original = Semear();
        var novoInicio = new DateTime(2026, 11, 20, 14, 0, 0);

        var dto = await _servico.DuplicarAsync(original.Id, novoInicio);

        Assert.NotEqual(original.Id, dto.Id);
        Assert.Equal("Reunião", dto.Titulo);
        Assert.Equal("Sala 2", dto.Descricao);
        Assert.Equal(novoInicio, dto.Inicio);
        Assert.Equal(novoInicio.AddMinutes(90), dto.Fim);
        Assert.Equal(2, _repositorio.Itens.Count);
    }

    [Fact]
    public async Task Duplicar_NaoAlteraOOriginal()
    {
        var original = Semear();

        await _servico.DuplicarAsync(original.Id, Inicio.AddDays(7));

        Assert.Equal(Inicio, original.Inicio);
        Assert.Equal(Fim, original.Fim);
    }

    [Theory]
    [InlineData(StatusAgendamento.Concluido)]
    [InlineData(StatusAgendamento.Cancelado)]
    public async Task Duplicar_DeConcluidoOuCancelado_GeraCopiaPendente(StatusAgendamento statusOriginal)
    {
        var original = Semear();
        if (statusOriginal == StatusAgendamento.Concluido)
            original.Concluir();
        else
            original.Cancelar();

        var dto = await _servico.DuplicarAsync(original.Id, Inicio.AddDays(7));

        Assert.Equal(StatusAgendamento.Pendente, dto.Status);
    }

    [Fact]
    public async Task Duplicar_GeraCopiaAvulsaSemSerie()
    {
        var original = Semear();
        original.AssociarSerie(Guid.NewGuid());

        var dto = await _servico.DuplicarAsync(original.Id, Inicio.AddDays(7));

        Assert.Null(dto.SerieId);
    }

    [Fact]
    public async Task Duplicar_Inexistente_LancaAgendamentoNaoEncontrado()
    {
        await Assert.ThrowsAsync<AgendamentoNaoEncontradoException>(() =>
            _servico.DuplicarAsync(99, Inicio));

        Assert.Equal(0, _repositorio.Gravacoes);
    }
}
