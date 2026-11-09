using DateCheck.Desktop.Servicos;
using DateCheck.Desktop.Tests.Apoio;
using DateCheck.Desktop.ViewModels;
using DateCheck.Domain.Enums;

namespace DateCheck.Desktop.Tests;

/// <summary>Itens do Notion (somente leitura) e de dia inteiro nas telas Hoje e Calendário e nas notificações.</summary>
public class OrigemExternaTelasTests
{
    private static readonly DateTime Hoje = new(2026, 11, 2);

    private readonly Ambiente _ambiente = new();

    private static DateTime H(int hora, int minuto = 0, int diasDepois = 0) =>
        Hoje.AddDays(diasDepois).AddHours(hora).AddMinutes(minuto);

    // ---------- tela Hoje ----------

    [Fact]
    public async Task Hoje_ItemExterno_TemSeloEAcoesDeEdicaoIndisponiveis()
    {
        _ambiente.SemearNotion("Dentista", H(15), H(16));
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        var item = Assert.Single(vm.Itens);
        Assert.True(item.EhExterno);
        Assert.True(item.TemSelo);
        Assert.Equal("Notion", item.Selo);
        Assert.False(item.PodeConcluir);
        Assert.False(item.PodeCancelar);
        Assert.False(item.PodeEditar);
        Assert.False(item.PodeDuplicar);
        Assert.False(item.PodeReabrir);
        Assert.True(item.PodeAbrirNaOrigem);
        Assert.Equal("Abrir no Notion", item.TextoAbrirNaOrigem);
    }

    [Fact]
    public async Task Hoje_ItemExternoConcluidoNoNotion_TambemNaoPodeSerReaberto()
    {
        _ambiente.SemearNotion("Dentista", H(15), H(16), status: StatusAgendamento.Concluido);
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        var item = Assert.Single(vm.Itens);
        Assert.Equal("Concluído", item.StatusTexto);
        Assert.False(item.PodeReabrir);
    }

    [Fact]
    public async Task Hoje_ItemLocal_SemSeloENemAbrirNaOrigem_ComTodasAsAcoes()
    {
        _ambiente.Semear("Reunião", H(15), H(16));
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        var item = Assert.Single(vm.Itens);
        Assert.False(item.TemSelo);
        Assert.Equal(string.Empty, item.Selo);
        Assert.False(item.PodeAbrirNaOrigem);
        Assert.True(item.PodeConcluir);
        Assert.True(item.PodeDuplicar);
    }

    [Fact]
    public async Task Hoje_ItemExternoSemUrl_NaoOfereceAbrirNaOrigem()
    {
        _ambiente.SemearNotion("Sem link", H(15), H(16), url: null);
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        Assert.False(vm.Itens.Single().PodeAbrirNaOrigem);
    }

    [Fact]
    public async Task AbrirNaOrigem_AbreAUrlDoItemNoNavegador()
    {
        _ambiente.SemearNotion("Dentista", H(15), H(16), url: "https://www.notion.so/dentista-123");
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        vm.AbrirNaOrigemCommand.Execute(vm.Itens.Single());

        Assert.Equal(["https://www.notion.so/dentista-123"], _ambiente.Links.Abertos);
        Assert.Empty(_ambiente.Dialogos.Erros);
    }

    [Fact]
    public async Task AbrirNaOrigem_QuandoNaoConsegueAbrir_MostraErro()
    {
        _ambiente.SemearNotion("Dentista", H(15), H(16));
        _ambiente.Links.Resultado = false;
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        vm.AbrirNaOrigemCommand.Execute(vm.Itens.Single());

        Assert.Single(_ambiente.Dialogos.Erros);
    }

    [Fact]
    public async Task AbrirNaOrigem_EmItemLocal_NaoFazNada()
    {
        _ambiente.Semear("Reunião", H(15), H(16));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        vm.AbrirNaOrigemCommand.Execute(vm.Itens.Single());

        Assert.Empty(_ambiente.Links.Abertos);
    }

    [Fact]
    public async Task DiaInteiro_MostraDiaInteiroNoLugarDoHorario()
    {
        _ambiente.SemearDiaInteiro("Feriado", Hoje);
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        Assert.Equal("Dia inteiro", vm.Itens.Single().Horario);
        Assert.True(vm.Itens.Single().DiaInteiro);
    }

    [Fact]
    public async Task DiaInteiro_VariosDias_MostraOIntervaloInclusivo()
    {
        _ambiente.SemearDiaInteiro("Viagem", Hoje.AddDays(-1), Hoje.AddDays(2));
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        Assert.Equal("Dia inteiro (01/11 – 04/11)", vm.Itens.Single().Horario);
    }

    [Fact]
    public async Task DiaInteiro_VemPrimeiroENaoContaComoProximoNemAtrasado()
    {
        _ambiente.Semear("Manhã", H(9), H(10)); // já passou: atrasado
        _ambiente.Semear("Tarde", H(15), H(16)); // o próximo
        _ambiente.SemearDiaInteiro("Feriado", Hoje);
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        Assert.Equal(["Feriado", "Manhã", "Tarde"], vm.Itens.Select(i => i.Titulo));
        var feriado = vm.Itens[0];
        Assert.False(feriado.Proximo);
        Assert.False(feriado.Atrasado);
        Assert.False(feriado.TemMarcador);
        Assert.True(vm.Itens.Single(i => i.Titulo == "Tarde").Proximo);
    }

    // ---------- link seguro ----------

    [Theory]
    [InlineData("https://www.notion.so/pagina-123", true)]
    [InlineData("http://exemplo.test/a", true)]
    [InlineData("HTTPS://WWW.NOTION.SO/X", true)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("C:\\Windows\\System32\\calc.exe", false)]
    [InlineData("calc.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("notion://www.notion.so/pagina", false)]
    [InlineData("ms-settings:", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AbridorDeLinks_SoAceitaHttpEHttps(string? url, bool esperado)
    {
        Assert.Equal(esperado, AbridorDeLinks.EhLinkWebValido(url, out _));
    }

    // ---------- calendário ----------

    [Fact]
    public async Task Calendario_ItemExternoTemSeloNoChip()
    {
        _ambiente.SemearNotion("Dentista", H(15, diasDepois: 3), H(16, diasDepois: 3));
        _ambiente.Semear("Reunião", H(9, diasDepois: 3), H(10, diasDepois: 3));
        var vm = _ambiente.CriarCalendario();

        await vm.AtivarAsync();

        var dia = vm.Dias.Single(d => d.Data == Hoje.AddDays(3));
        var reuniao = dia.Chips.Single(c => c.Texto.Contains("Reunião"));
        var dentista = dia.Chips.Single(c => c.Texto.Contains("Dentista"));
        Assert.False(reuniao.TemSelo);
        Assert.True(dentista.TemSelo);
        Assert.Equal("Notion", dentista.Selo);
        Assert.Equal("15:00 Dentista", dentista.Texto);
    }

    [Fact]
    public async Task Calendario_DiaInteiro_VemPrimeiroSemHorarioEEmTodosOsDiasQueOcupa()
    {
        _ambiente.Semear("Meia-noite", H(0, diasDepois: 1), H(0, 30, diasDepois: 1));
        _ambiente.SemearDiaInteiro("Viagem", Hoje.AddDays(1), Hoje.AddDays(2));
        var vm = _ambiente.CriarCalendario();

        await vm.AtivarAsync();

        var dia1 = vm.Dias.Single(d => d.Data == Hoje.AddDays(1));
        var dia2 = vm.Dias.Single(d => d.Data == Hoje.AddDays(2));
        var dia3 = vm.Dias.Single(d => d.Data == Hoje.AddDays(3));
        Assert.Equal(["Viagem", "00:00 Meia-noite"], dia1.Chips.Select(c => c.Texto));
        Assert.Equal(["Viagem"], dia2.Chips.Select(c => c.Texto));
        Assert.Empty(dia3.Chips);
    }

    // ---------- notificações ----------

    [Fact]
    public async Task Notificacao_DiaInteiroNuncaEhAvisado_MasItemExternoComHorarioEh()
    {
        var notificador = new FakeNotificador();
        var servico = new Desktop.Notificacoes.NotificacaoService(
            _ambiente.Agendamentos, notificador, new FakePreferenciasService(), _ambiente.Relogio,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Desktop.Notificacoes.NotificacaoService>.Instance);
        // Agora é 13:30 (padrão: avisa 10 min antes). Um dia inteiro "começa" à meia-noite de amanhã.
        _ambiente.DefinirAgora(Hoje.AddDays(1).AddMinutes(-5));
        _ambiente.SemearDiaInteiro("Feriado", Hoje.AddDays(1));
        _ambiente.SemearNotion("Dentista", Hoje.AddDays(1).AddMinutes(5), Hoje.AddDays(1).AddMinutes(35));

        await servico.VerificarAsync();

        Assert.Equal(["Dentista"], notificador.Titulos);
    }
}
