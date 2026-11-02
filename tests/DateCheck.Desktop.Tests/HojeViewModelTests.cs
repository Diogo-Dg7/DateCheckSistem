using DateCheck.Desktop.Tests.Apoio;
using DateCheck.Domain.Enums;

namespace DateCheck.Desktop.Tests;

public class HojeViewModelTests
{
    private static readonly DateTime Hoje = new(2026, 11, 2);

    private readonly Ambiente _ambiente = new();

    private static DateTime H(int hora, int minuto = 0, int diasDepois = 0) =>
        Hoje.AddDays(diasDepois).AddHours(hora).AddMinutes(minuto);

    [Fact]
    public async Task Ativar_CarregaOsCompromissosDoDiaEmOrdemDeHorario()
    {
        _ambiente.Semear("Tarde", H(15), H(16));
        _ambiente.Semear("Manhã", H(9), H(10));
        _ambiente.Semear("Amanhã", H(9, diasDepois: 1), H(10, diasDepois: 1));
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        Assert.Equal(["Manhã", "Tarde"], vm.Itens.Select(i => i.Titulo));
        Assert.False(vm.EstaVazio);
    }

    [Fact]
    public async Task DiaSemCompromissos_FicaVazio()
    {
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        Assert.Empty(vm.Itens);
        Assert.True(vm.EstaVazio);
    }

    [Fact]
    public async Task Cabecalho_MostraHojeEADataPorExtensoEmPortugues()
    {
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        Assert.Equal("Hoje", vm.Titulo);
        Assert.Equal("segunda-feira, 2 de novembro de 2026", vm.Subtitulo);
        Assert.True(vm.EhHoje);
    }

    [Fact]
    public async Task Destaques_AtrasadoEProximo()
    {
        _ambiente.Semear("Atrasado", H(8), H(9));
        _ambiente.Semear("Próximo", H(15), H(16));
        _ambiente.Semear("Depois", H(18), H(19));
        var vm = _ambiente.CriarHoje();

        await vm.AtivarAsync();

        Assert.True(vm.Itens.Single(i => i.Titulo == "Atrasado").Atrasado);
        Assert.Equal(["Próximo"], vm.Itens.Where(i => i.Proximo).Select(i => i.Titulo));
        Assert.Equal("Atrasado", vm.Itens[0].Marcador);
        Assert.Equal("Próximo", vm.Itens[1].Marcador);
        Assert.Equal(string.Empty, vm.Itens[2].Marcador);
    }

    [Fact]
    public async Task AtualizacaoPeriodica_RecalculaProximoEAtrasadosComOPassarDoTempo()
    {
        _ambiente.Semear("Reunião", H(14), H(15));
        _ambiente.Semear("Jantar", H(20), H(21));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        Assert.True(vm.Itens.Single(i => i.Titulo == "Reunião").Proximo);

        _ambiente.DefinirAgora(H(15, 30));
        _ambiente.Relogio.Advance(TimeSpan.FromSeconds(30));

        Assert.True(vm.Itens.Single(i => i.Titulo == "Reunião").Atrasado);
        Assert.False(vm.Itens.Single(i => i.Titulo == "Reunião").Proximo);
        Assert.True(vm.Itens.Single(i => i.Titulo == "Jantar").Proximo);
    }

    [Fact]
    public async Task AtualizacaoPeriodica_AcontecaACada30Segundos()
    {
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        var antes = _ambiente.Repositorio.Consultas;

        _ambiente.Relogio.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(antes, _ambiente.Repositorio.Consultas);

        _ambiente.Relogio.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(antes + 1, _ambiente.Repositorio.Consultas);

        _ambiente.Relogio.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(antes + 2, _ambiente.Repositorio.Consultas);
    }

    [Fact]
    public async Task Desativar_ParaAAtualizacaoPeriodica()
    {
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        vm.Desativar();
        var antes = _ambiente.Repositorio.Consultas;

        _ambiente.Relogio.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(antes, _ambiente.Repositorio.Consultas);
    }

    [Fact]
    public async Task AtualizacaoPeriodica_SemMudancas_NaoRecriaAColecao()
    {
        _ambiente.Semear("Reunião", H(15), H(16));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        var primeiro = vm.Itens[0];

        _ambiente.Relogio.Advance(TimeSpan.FromSeconds(30));

        Assert.Same(primeiro, vm.Itens[0]); // mantém a rolagem e a seleção do usuário
    }

    [Fact]
    public async Task QuandoOBancoFalhaNaAtualizacaoPeriodica_MantemAListaSemErro()
    {
        _ambiente.Semear("Reunião", H(15), H(16));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        var antes = _ambiente.Repositorio.Consultas;
        _ambiente.Repositorio.FalharConsultas = true;

        _ambiente.Relogio.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(antes + 1, _ambiente.Repositorio.Consultas); // tentou atualizar e falhou
        Assert.Single(vm.Itens);
        Assert.Empty(_ambiente.Dialogos.Erros);
    }

    [Fact]
    public async Task Virada_DeDiaQuandoAcompanhandoHoje_AtualizaOCabecalho()
    {
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        _ambiente.DefinirAgora(H(0, 1, diasDepois: 1));
        _ambiente.Relogio.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(Hoje.AddDays(1), vm.Dia);
        Assert.Equal("terça-feira, 3 de novembro de 2026", vm.Subtitulo);
        Assert.Equal("Hoje", vm.Titulo);
    }

    [Fact]
    public async Task DiaAnteriorESeguinte_TrocamODiaEORotulo()
    {
        _ambiente.Semear("De ontem", H(9, diasDepois: -1), H(10, diasDepois: -1));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        await vm.DiaAnteriorCommand.ExecuteAsync(null);

        Assert.Equal("Domingo", vm.Titulo);
        Assert.False(vm.EhHoje);
        Assert.Equal(["De ontem"], vm.Itens.Select(i => i.Titulo));

        await vm.DiaSeguinteCommand.ExecuteAsync(null);
        Assert.Equal("Hoje", vm.Titulo);
    }

    [Fact]
    public async Task OutroDia_NaoAcompanhaORelogioENaoMarcaProximo()
    {
        _ambiente.Semear("Amanhã", H(9, diasDepois: 1), H(10, diasDepois: 1));
        var vm = _ambiente.CriarHoje();
        vm.DefinirDia(Hoje.AddDays(1));
        await vm.AtivarAsync();

        _ambiente.Relogio.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(Hoje.AddDays(1), vm.Dia);
        Assert.DoesNotContain(vm.Itens, i => i.Proximo);
    }

    [Fact]
    public async Task VoltarParaHoje_RetornaAoDiaAtual()
    {
        var vm = _ambiente.CriarHoje();
        vm.DefinirDia(Hoje.AddDays(5));
        await vm.AtivarAsync();

        await vm.VoltarParaHojeCommand.ExecuteAsync(null);

        Assert.Equal(Hoje, vm.Dia);
        Assert.True(vm.EhHoje);
    }

    [Fact]
    public async Task DefinirDia_Nulo_VoltaAAcompanharHoje()
    {
        var vm = _ambiente.CriarHoje();
        vm.DefinirDia(Hoje.AddDays(3));

        vm.DefinirDia(null);

        Assert.Equal(Hoje, vm.Dia);
        await vm.AtivarAsync();
        Assert.True(vm.EhHoje);
    }

    [Fact]
    public async Task Concluir_MudaOStatusEAtualizaALista()
    {
        var agendamento = _ambiente.Semear("Tarefa", H(15), H(16));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        await vm.ConcluirCommand.ExecuteAsync(vm.Itens[0]);

        Assert.Equal(StatusAgendamento.Concluido, agendamento.Status);
        Assert.Equal(StatusAgendamento.Concluido, vm.Itens[0].Status);
        Assert.False(vm.Itens[0].PodeConcluir);
        Assert.True(vm.Itens[0].PodeReabrir);
    }

    [Fact]
    public async Task Cancelar_ConfirmadoMudaOStatus()
    {
        var agendamento = _ambiente.Semear("Tarefa", H(15), H(16));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        await vm.CancelarCommand.ExecuteAsync(vm.Itens[0]);

        Assert.Equal(StatusAgendamento.Cancelado, agendamento.Status);
        Assert.Contains("Tarefa", _ambiente.Dialogos.Confirmacoes.Single().Mensagem);
    }

    [Fact]
    public async Task Cancelar_RecusadoNaoAlteraNada()
    {
        var agendamento = _ambiente.Semear("Tarefa", H(15), H(16));
        _ambiente.Dialogos.RespostaConfirmar = false;
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        await vm.CancelarCommand.ExecuteAsync(vm.Itens[0]);

        Assert.Equal(StatusAgendamento.Pendente, agendamento.Status);
    }

    [Fact]
    public async Task Reabrir_VoltaOCompromissoCanceladoParaPendente()
    {
        var agendamento = _ambiente.Semear("Tarefa", H(15), H(16));
        agendamento.Cancelar();
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        await vm.ReabrirCommand.ExecuteAsync(vm.Itens[0]);

        Assert.Equal(StatusAgendamento.Pendente, agendamento.Status);
        Assert.True(vm.Itens[0].PodeConcluir);
    }

    [Fact]
    public async Task RegraDeNegocioViolada_MostraAMensagemEAtualizaATela()
    {
        var agendamento = _ambiente.Semear("Tarefa", H(15), H(16));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        agendamento.Cancelar(); // mudou "por fora" enquanto a tela estava aberta

        await vm.ConcluirCommand.ExecuteAsync(vm.Itens[0]);

        Assert.Equal(["Não é possível concluir um agendamento cancelado."], _ambiente.Dialogos.Erros);
        Assert.Equal(StatusAgendamento.Cancelado, vm.Itens[0].Status);
    }

    [Fact]
    public async Task Comandos_ComItemNulo_NaoFazemNada()
    {
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        await vm.ConcluirCommand.ExecuteAsync(null);
        await vm.CancelarCommand.ExecuteAsync(null);
        await vm.ReabrirCommand.ExecuteAsync(null);
        await vm.EditarCommand.ExecuteAsync(null);
        await vm.DuplicarCommand.ExecuteAsync(null);

        Assert.Empty(_ambiente.Dialogos.Erros);
        Assert.Empty(_ambiente.Dialogos.Confirmacoes);
        Assert.Empty(_ambiente.Dialogos.FormulariosAbertos);
    }

    [Fact]
    public async Task Novo_AbreOFormularioNoDiaExibidoEAtualizaAoSalvar()
    {
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        _ambiente.Dialogos.ResultadoDoFormulario = true;
        _ambiente.Semear("Criado no formulário", H(16), H(17)); // simula o que o formulário gravou

        await vm.NovoCommand.ExecuteAsync(null);

        var abertura = Assert.Single(_ambiente.Dialogos.FormulariosAbertos);
        Assert.Null(abertura.Id);
        Assert.Equal(Hoje, abertura.Dia);
        Assert.Equal(["Criado no formulário"], vm.Itens.Select(i => i.Titulo));
    }

    [Fact]
    public async Task Novo_CanceladoNaoRecarrega()
    {
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        var antes = _ambiente.Repositorio.Consultas;
        _ambiente.Dialogos.ResultadoDoFormulario = false;

        await vm.NovoCommand.ExecuteAsync(null);

        Assert.Equal(antes, _ambiente.Repositorio.Consultas);
    }

    [Fact]
    public async Task Editar_AbreOFormularioComOIdDoCompromisso()
    {
        var agendamento = _ambiente.Semear("Tarefa", H(15), H(16));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();

        await vm.EditarCommand.ExecuteAsync(vm.Itens[0]);

        var abertura = Assert.Single(_ambiente.Dialogos.FormulariosAbertos);
        Assert.Equal(agendamento.Id, abertura.Id);
        Assert.Null(abertura.Dia);
    }

    [Fact]
    public async Task Duplicar_CriaCopiaNaNovaDataEMostraODiaDaCopia()
    {
        _ambiente.Semear("Reunião", H(15), H(16, 30));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        _ambiente.Dialogos.DataEscolhida = H(10, diasDepois: 7);

        await vm.DuplicarCommand.ExecuteAsync(vm.Itens[0]);

        Assert.Equal(2, _ambiente.Repositorio.Itens.Count);
        var copia = _ambiente.Repositorio.Itens[1];
        Assert.Equal(H(10, diasDepois: 7), copia.Inicio);
        Assert.Equal(H(11, 30, diasDepois: 7), copia.Fim); // mesma duração de 1h30
        Assert.Equal(Hoje.AddDays(7), vm.Dia);
        Assert.Equal(["Reunião"], vm.Itens.Select(i => i.Titulo));
        Assert.Equal(H(15).AddDays(1), _ambiente.Dialogos.DatasPedidas.Single().Inicial); // sugere o dia seguinte
    }

    [Fact]
    public async Task Duplicar_UsuarioCancelaODialogo_NaoCriaNada()
    {
        _ambiente.Semear("Reunião", H(15), H(16));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        _ambiente.Dialogos.DataEscolhida = null;

        await vm.DuplicarCommand.ExecuteAsync(vm.Itens[0]);

        Assert.Single(_ambiente.Repositorio.Itens);
    }

    [Fact]
    public async Task Duplicar_ComConflito_PerguntaEOUsuarioPodeRecusar()
    {
        _ambiente.Semear("Reunião", H(15), H(16));
        _ambiente.Semear("Outro", H(10, diasDepois: 2), H(11, diasDepois: 2));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        _ambiente.Dialogos.DataEscolhida = H(10, 30, diasDepois: 2);
        _ambiente.Dialogos.RespostaConfirmar = false;

        await vm.DuplicarCommand.ExecuteAsync(vm.Itens[0]);

        Assert.Contains("Outro", _ambiente.Dialogos.Confirmacoes.Single().Mensagem);
        Assert.Equal(2, _ambiente.Repositorio.Itens.Count);
        Assert.Equal(Hoje, vm.Dia);
    }

    [Fact]
    public async Task Duplicar_ComConflito_UsuarioConfirma_CriaAMesmoAssim()
    {
        _ambiente.Semear("Reunião", H(15), H(16));
        _ambiente.Semear("Outro", H(10, diasDepois: 2), H(11, diasDepois: 2));
        var vm = _ambiente.CriarHoje();
        await vm.AtivarAsync();
        _ambiente.Dialogos.DataEscolhida = H(10, 30, diasDepois: 2);
        _ambiente.Dialogos.RespostaConfirmar = true;

        await vm.DuplicarCommand.ExecuteAsync(vm.Itens[0]);

        Assert.Equal(3, _ambiente.Repositorio.Itens.Count);
    }

    [Fact]
    public async Task IrParaCalendario_PedeANavegacao()
    {
        var vm = _ambiente.CriarHoje();

        await vm.IrParaCalendarioCommand.ExecuteAsync(null);

        Assert.Equal(1, _ambiente.Navegacao.CalendarioMostrado);
    }
}
