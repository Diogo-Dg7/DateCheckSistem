using DateCheck.Application.Servicos;
using DateCheck.Application.Tests.Fakes;
using DateCheck.Domain.Entidades;

namespace DateCheck.Application.Tests;

public class ConflitoServiceTests
{
    private static readonly DateTime Dia = new(2026, 11, 10);

    private readonly FakeAgendamentoRepository _repositorio = new();
    private readonly ConflitoService _servico;

    public ConflitoServiceTests()
    {
        _servico = new ConflitoService(_repositorio);
    }

    private static DateTime H(int hora, int minuto = 0) => Dia.AddHours(hora).AddMinutes(minuto);

    private Agendamento Semear(string titulo, DateTime inicio, DateTime fim) =>
        _repositorio.Semear(new Agendamento(titulo, inicio, fim));

    [Fact]
    public async Task SemAgendamentos_NaoHaConflito()
    {
        var conflitos = await _servico.BuscarConflitosAsync(H(9), H(10));

        Assert.Empty(conflitos);
    }

    [Fact]
    public async Task PeriodosSobrepostos_Conflitam()
    {
        Semear("Existente", H(9), H(10));

        var conflitos = await _servico.BuscarConflitosAsync(H(9, 30), H(10, 30));

        Assert.Single(conflitos);
        Assert.Equal("Existente", conflitos[0].Titulo);
    }

    [Fact]
    public async Task PeriodoContidoEmOutro_Conflita()
    {
        Semear("Longo", H(8), H(12));

        Assert.Single(await _servico.BuscarConflitosAsync(H(9), H(10)));
    }

    [Fact]
    public async Task PeriodoQueContemOutro_Conflita()
    {
        Semear("Curto", H(9), H(10));

        Assert.Single(await _servico.BuscarConflitosAsync(H(8), H(12)));
    }

    [Fact]
    public async Task IntervalosEncostadosNoFim_NaoConflitam()
    {
        Semear("Existente", H(9), H(10));

        Assert.Empty(await _servico.BuscarConflitosAsync(H(10), H(11)));
    }

    [Fact]
    public async Task IntervalosEncostadosNoInicio_NaoConflitam()
    {
        Semear("Existente", H(10), H(11));

        Assert.Empty(await _servico.BuscarConflitosAsync(H(9), H(10)));
    }

    [Fact]
    public async Task AgendamentoCancelado_EhIgnorado()
    {
        var existente = Semear("Cancelado", H(9), H(10));
        existente.Cancelar();

        Assert.Empty(await _servico.BuscarConflitosAsync(H(9), H(10)));
    }

    [Fact]
    public async Task AgendamentoConcluido_AindaConflita()
    {
        var existente = Semear("Concluído", H(9), H(10));
        existente.Concluir();

        Assert.Single(await _servico.BuscarConflitosAsync(H(9), H(10)));
    }

    [Fact]
    public async Task AoEditar_IgnoraOProprioAgendamento()
    {
        var proprio = Semear("Próprio", H(9), H(10));

        var conflitos = await _servico.BuscarConflitosAsync(H(9, 30), H(10, 30), ignorarId: proprio.Id);

        Assert.Empty(conflitos);
    }

    [Fact]
    public async Task AoEditar_AindaApontaOutrosAgendamentos()
    {
        var proprio = Semear("Próprio", H(9), H(10));
        Semear("Outro", H(10), H(11));

        var conflitos = await _servico.BuscarConflitosAsync(H(9, 30), H(10, 30), ignorarId: proprio.Id);

        Assert.Equal(["Outro"], conflitos.Select(c => c.Titulo));
    }

    [Fact]
    public async Task VariosConflitos_VoltamOrdenadosPorInicio()
    {
        Semear("Segundo", H(10), H(11));
        Semear("Primeiro", H(9), H(10, 30));

        var conflitos = await _servico.BuscarConflitosAsync(H(9), H(11));

        Assert.Equal(["Primeiro", "Segundo"], conflitos.Select(c => c.Titulo));
    }

    [Fact]
    public async Task AgendamentoQueAtravessaAMeiaNoite_ConflitaNoDiaSeguinte()
    {
        Semear("Plantão", H(22), H(22).AddHours(6));

        var conflitos = await _servico.BuscarConflitosAsync(Dia.AddDays(1).AddHours(1), Dia.AddDays(1).AddHours(2));

        Assert.Single(conflitos);
    }
}
