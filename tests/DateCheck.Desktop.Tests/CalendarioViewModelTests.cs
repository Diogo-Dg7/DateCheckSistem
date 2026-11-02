using DateCheck.Desktop.Tests.Apoio;
using DateCheck.Desktop.ViewModels;

namespace DateCheck.Desktop.Tests;

public class CalendarioViewModelTests
{
    private readonly Ambiente _ambiente = new();

    [Fact]
    public async Task Ativar_AbreNoMesAtualComAGradeDe42Dias()
    {
        var vm = _ambiente.CriarCalendario();

        await vm.AtivarAsync();

        Assert.Equal("Novembro de 2026", vm.Titulo);
        Assert.Equal(42, vm.Dias.Count);
        Assert.Equal(new DateTime(2026, 11, 1), vm.Dias[0].Data);
    }

    [Fact]
    public async Task Carregar_MostraCompromissosDoMesInclusiveDosDiasVizinhos()
    {
        _ambiente.Semear("Dentro", new DateTime(2026, 11, 5, 9, 0, 0), new DateTime(2026, 11, 5, 10, 0, 0));
        _ambiente.Semear("Dezembro", new DateTime(2026, 12, 5, 9, 0, 0), new DateTime(2026, 12, 5, 10, 0, 0));
        _ambiente.Semear("Fora da grade", new DateTime(2026, 12, 20, 9, 0, 0), new DateTime(2026, 12, 20, 10, 0, 0));
        var vm = _ambiente.CriarCalendario();

        await vm.AtivarAsync();

        Assert.Equal(1, vm.Dias.Single(d => d.Data == new DateTime(2026, 11, 5)).Total);
        Assert.Equal(1, vm.Dias.Single(d => d.Data == new DateTime(2026, 12, 5)).Total);
        Assert.Equal(2, vm.Dias.Sum(d => d.Total));
    }

    [Fact]
    public async Task MesSeguinteEAnterior_NavegamERecarregam()
    {
        _ambiente.Semear("De dezembro", new DateTime(2026, 12, 10, 9, 0, 0), new DateTime(2026, 12, 10, 10, 0, 0));
        var vm = _ambiente.CriarCalendario();
        await vm.AtivarAsync();

        await vm.MesSeguinteCommand.ExecuteAsync(null);

        Assert.Equal("Dezembro de 2026", vm.Titulo);
        Assert.Equal(1, vm.Dias.Single(d => d.Data == new DateTime(2026, 12, 10)).Total);

        await vm.MesAnteriorCommand.ExecuteAsync(null);

        Assert.Equal("Novembro de 2026", vm.Titulo);
    }

    [Fact]
    public async Task Navegacao_CruzaAViradaDeAno()
    {
        var vm = _ambiente.CriarCalendario();
        await vm.AtivarAsync();

        await vm.MesSeguinteCommand.ExecuteAsync(null);
        await vm.MesSeguinteCommand.ExecuteAsync(null);
        Assert.Equal("Janeiro de 2027", vm.Titulo);

        await vm.MesAnteriorCommand.ExecuteAsync(null);
        await vm.MesAnteriorCommand.ExecuteAsync(null);
        await vm.MesAnteriorCommand.ExecuteAsync(null);
        Assert.Equal("Outubro de 2026", vm.Titulo);
    }

    [Fact]
    public async Task IrParaMesAtual_VoltaParaOMesDeHoje()
    {
        var vm = _ambiente.CriarCalendario();
        await vm.AtivarAsync();
        await vm.MesSeguinteCommand.ExecuteAsync(null);
        await vm.MesSeguinteCommand.ExecuteAsync(null);

        await vm.IrParaMesAtualCommand.ExecuteAsync(null);

        Assert.Equal("Novembro de 2026", vm.Titulo);
    }

    [Fact]
    public async Task ReativarTela_MantemOMesQueOUsuarioEstavaVendo()
    {
        var vm = _ambiente.CriarCalendario();
        await vm.AtivarAsync();
        await vm.MesSeguinteCommand.ExecuteAsync(null);
        vm.Desativar();

        await vm.AtivarAsync();

        Assert.Equal("Dezembro de 2026", vm.Titulo);
    }

    [Fact]
    public async Task ClicarNoDia_AbreATelaHojeNaqueleDia()
    {
        var vm = _ambiente.CriarCalendario();
        await vm.AtivarAsync();
        var dia = vm.Dias.Single(d => d.Data == new DateTime(2026, 11, 18));

        await vm.AbrirDiaCommand.ExecuteAsync(dia);

        Assert.Equal([new DateTime(2026, 11, 18)], _ambiente.Navegacao.HojeMostrado.Select(d => d));
    }

    [Fact]
    public async Task ClicarNoDiaDeOutroMes_AbreOProprioDia()
    {
        var vm = _ambiente.CriarCalendario();
        await vm.AtivarAsync();
        var dia = vm.Dias[^1]; // 12/12/2026, célula do mês seguinte

        await vm.AbrirDiaCommand.ExecuteAsync(dia);

        Assert.Equal(new DateTime(2026, 12, 12), _ambiente.Navegacao.HojeMostrado.Single());
    }

    [Fact]
    public async Task IrParaHoje_PedeATelaHojeSemDiaEspecifico()
    {
        var vm = _ambiente.CriarCalendario();

        await vm.IrParaHojeCommand.ExecuteAsync(null);

        Assert.Equal([null], _ambiente.Navegacao.HojeMostrado);
    }

    [Fact]
    public void DiasDaSemana_ComecamNoDomingo()
    {
        Assert.Equal(["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"], CalendarioViewModel.DiasDaSemana);
    }
}
