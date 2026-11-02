using DateCheck.Application.Dtos;
using DateCheck.Desktop.Tests.Apoio;
using DateCheck.Desktop.ViewModels;
using DateCheck.Domain.Enums;

namespace DateCheck.Desktop.Tests;

public class AgendamentoFormViewModelTests
{
    private static readonly DateTime Dia = new(2026, 11, 10);

    private readonly Ambiente _ambiente = new();
    private readonly List<bool> _fechamentos = [];

    private AgendamentoFormViewModel CriarFormulario(int? id = null, DateTime? diaSugerido = null)
    {
        var vm = _ambiente.CriarFormulario();
        vm.FechamentoSolicitado += _fechamentos.Add;
        vm.Preparar(id, diaSugerido);
        return vm;
    }

    private static void Preencher(AgendamentoFormViewModel vm, string titulo = "Dentista",
        string inicio = "14:00", string fim = "15:00")
    {
        vm.Titulo = titulo;
        vm.DataInicio = Dia;
        vm.HoraInicio = inicio;
        vm.DataFim = Dia;
        vm.HoraFim = fim;
    }

    // ---------- valores iniciais ----------

    [Fact]
    public async Task NovoHoje_ComecaNaProximaHoraCheiaComUmaHoraDeDuracao()
    {
        var vm = CriarFormulario(diaSugerido: Ambiente.Agora.Date); // agora = 13:30

        await vm.CarregarAsync();

        Assert.False(vm.EhEdicao);
        Assert.Equal("Novo compromisso", vm.TituloDaJanela);
        Assert.Equal(Ambiente.Agora.Date, vm.DataInicio);
        Assert.Equal("14:00", vm.HoraInicio);
        Assert.Equal("15:00", vm.HoraFim);
    }

    [Fact]
    public async Task NovoEmOutroDia_ComecaAs9()
    {
        var vm = CriarFormulario(diaSugerido: Dia);

        await vm.CarregarAsync();

        Assert.Equal(Dia, vm.DataInicio);
        Assert.Equal("09:00", vm.HoraInicio);
        Assert.Equal("10:00", vm.HoraFim);
    }

    [Fact]
    public async Task NovoHojeTardeDaNoite_NaoVazaParaOutroDia()
    {
        _ambiente.DefinirAgora(Ambiente.Agora.Date.AddHours(23).AddMinutes(30));
        var vm = CriarFormulario(diaSugerido: Ambiente.Agora.Date);

        await vm.CarregarAsync();

        Assert.Equal(Ambiente.Agora.Date, vm.DataInicio);
        Assert.Equal("23:00", vm.HoraInicio);
    }

    [Fact]
    public async Task Edicao_CarregaOsDadosDoCompromisso()
    {
        var existente = _ambiente.Semear("Reunião", Dia.AddHours(9), Dia.AddHours(10).AddMinutes(30), "Sala 2");
        var vm = CriarFormulario(existente.Id);

        await vm.CarregarAsync();

        Assert.True(vm.EhEdicao);
        Assert.Equal("Editar compromisso", vm.TituloDaJanela);
        Assert.Equal("Reunião", vm.Titulo);
        Assert.Equal("Sala 2", vm.Descricao);
        Assert.Equal(Dia, vm.DataInicio);
        Assert.Equal("09:00", vm.HoraInicio);
        Assert.Equal("10:30", vm.HoraFim);
    }

    // ---------- salvar ----------

    [Fact]
    public async Task Salvar_NovoCompromisso_GravaEFechaComSucesso()
    {
        var vm = CriarFormulario();
        Preencher(vm);
        vm.Descricao = "Rua A";

        await vm.SalvarCommand.ExecuteAsync(null);

        var salvo = Assert.Single(_ambiente.Repositorio.Itens);
        Assert.Equal("Dentista", salvo.Titulo);
        Assert.Equal("Rua A", salvo.Descricao);
        Assert.Equal(Dia.AddHours(14), salvo.Inicio);
        Assert.Equal(Dia.AddHours(15), salvo.Fim);
        Assert.Equal([true], _fechamentos);
        Assert.Empty(_ambiente.Dialogos.Erros);
    }

    [Fact]
    public async Task Salvar_Edicao_AtualizaOCompromissoExistente()
    {
        var existente = _ambiente.Semear("Antigo", Dia.AddHours(9), Dia.AddHours(10));
        var vm = CriarFormulario(existente.Id);
        await vm.CarregarAsync();
        vm.Titulo = "Novo";
        vm.HoraInicio = "11:15";
        vm.HoraFim = "12:00";

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Single(_ambiente.Repositorio.Itens);
        Assert.Equal("Novo", existente.Titulo);
        Assert.Equal(Dia.AddHours(11).AddMinutes(15), existente.Inicio);
        Assert.Equal([true], _fechamentos);
    }

    [Theory]
    [InlineData("9:00", "9:30")]
    [InlineData("09:00", "09:30")]
    public async Task Salvar_AceitaHoraComUmOuDoisDigitos(string inicio, string fim)
    {
        var vm = CriarFormulario();
        Preencher(vm, inicio: inicio, fim: fim);

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Equal(Dia.AddHours(9), _ambiente.Repositorio.Itens.Single().Inicio);
    }

    [Theory]
    [InlineData("25:00", "26:00")]
    [InlineData("abc", "15:00")]
    [InlineData("14:00", "")]
    [InlineData("1400", "15:00")]
    public async Task Salvar_HoraInvalida_AvisaENaoGrava(string inicio, string fim)
    {
        var vm = CriarFormulario();
        Preencher(vm, inicio: inicio, fim: fim);

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Contains("HH:mm", Assert.Single(_ambiente.Dialogos.Avisos));
        Assert.Empty(_ambiente.Repositorio.Itens);
        Assert.Empty(_fechamentos);
    }

    [Fact]
    public async Task Salvar_SemData_AvisaENaoGrava()
    {
        var vm = CriarFormulario();
        Preencher(vm);
        vm.DataFim = null;

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Single(_ambiente.Dialogos.Avisos);
        Assert.Empty(_ambiente.Repositorio.Itens);
    }

    [Theory]
    [InlineData("15:00", "14:00")]
    [InlineData("14:00", "14:00")]
    public async Task Salvar_FimNaoPosteriorAoInicio_AvisaSemPerguntarConflitoENaoGrava(string inicio, string fim)
    {
        _ambiente.Semear("Existente", Dia.AddHours(13), Dia.AddHours(16));
        var vm = CriarFormulario();
        Preencher(vm, inicio: inicio, fim: fim);

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Equal(["O fim deve ser posterior ao início."], _ambiente.Dialogos.Avisos);
        Assert.Empty(_ambiente.Dialogos.Confirmacoes);
        Assert.Single(_ambiente.Repositorio.Itens);
    }

    [Fact]
    public async Task Salvar_SemTitulo_MostraAMensagemDoDominioENaoFecha()
    {
        var vm = CriarFormulario();
        Preencher(vm, titulo: "   ");

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Equal(["O título é obrigatório."], _ambiente.Dialogos.Erros);
        Assert.Empty(_ambiente.Repositorio.Itens);
        Assert.Empty(_fechamentos);
    }

    [Fact]
    public async Task Cancelar_FechaSemSalvar()
    {
        var vm = CriarFormulario();
        Preencher(vm);

        vm.CancelarCommand.Execute(null);

        Assert.Equal([false], _fechamentos);
        Assert.Empty(_ambiente.Repositorio.Itens);
        await Task.CompletedTask;
    }

    // ---------- conflitos ----------

    [Fact]
    public async Task Conflito_PerguntaSalvarMesmoAssim_EConfirmadoGrava()
    {
        _ambiente.Semear("Existente", Dia.AddHours(14), Dia.AddHours(15));
        var vm = CriarFormulario();
        Preencher(vm, inicio: "14:30", fim: "15:30");
        _ambiente.Dialogos.RespostaConfirmar = true;

        await vm.SalvarCommand.ExecuteAsync(null);

        var confirmacao = Assert.Single(_ambiente.Dialogos.Confirmacoes);
        Assert.Equal("Conflito de horários", confirmacao.Titulo);
        Assert.Contains("Existente", confirmacao.Mensagem);
        Assert.Contains("Salvar mesmo assim?", confirmacao.Mensagem);
        Assert.Equal(2, _ambiente.Repositorio.Itens.Count);
        Assert.Equal([true], _fechamentos);
    }

    [Fact]
    public async Task Conflito_Recusado_NaoGravaENaoFecha()
    {
        _ambiente.Semear("Existente", Dia.AddHours(14), Dia.AddHours(15));
        var vm = CriarFormulario();
        Preencher(vm, inicio: "14:30", fim: "15:30");
        _ambiente.Dialogos.RespostaConfirmar = false;

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Single(_ambiente.Repositorio.Itens);
        Assert.Empty(_fechamentos);
    }

    [Fact]
    public async Task HorarioEncostado_NaoEhConflito()
    {
        _ambiente.Semear("Existente", Dia.AddHours(13), Dia.AddHours(14));
        var vm = CriarFormulario();
        Preencher(vm, inicio: "14:00", fim: "15:00");

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Empty(_ambiente.Dialogos.Confirmacoes);
        Assert.Equal(2, _ambiente.Repositorio.Itens.Count);
    }

    [Fact]
    public async Task Edicao_NaoConflitaComOProprioCompromisso()
    {
        var existente = _ambiente.Semear("Reunião", Dia.AddHours(14), Dia.AddHours(15));
        var vm = CriarFormulario(existente.Id);
        await vm.CarregarAsync();
        vm.HoraFim = "15:30";

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Empty(_ambiente.Dialogos.Confirmacoes);
        Assert.Equal(Dia.AddHours(15).AddMinutes(30), existente.Fim);
    }

    [Fact]
    public async Task Conflito_ListaNoMaximoCincoEResumeOResto()
    {
        for (var i = 0; i < 7; i++)
            _ambiente.Semear($"Item {i}", Dia.AddHours(14), Dia.AddHours(15));
        var vm = CriarFormulario();
        Preencher(vm);

        await vm.SalvarCommand.ExecuteAsync(null);

        var mensagem = Assert.Single(_ambiente.Dialogos.Confirmacoes).Mensagem;
        Assert.Equal(5, mensagem.Split('\n').Count(l => l.StartsWith("• Item") || l.Contains("  Item")));
        Assert.Contains("e mais 2", mensagem);
    }

    // ---------- repetição ----------

    [Fact]
    public async Task Repetir_SemDataFinal_AvisaENaoGravaNada()
    {
        var vm = CriarFormulario();
        Preencher(vm);
        vm.Repetir = true;
        vm.RepetirAte = null;

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Single(_ambiente.Dialogos.Avisos);
        Assert.Empty(_ambiente.Repositorio.Itens);
    }

    [Fact]
    public async Task Repetir_MostraAPreviaEGeraAsCopiasQuandoConfirmado()
    {
        var vm = CriarFormulario();
        Preencher(vm);
        vm.Repetir = true;
        vm.Frequencia = FrequenciaRepeticao.Semanal;
        vm.RepetirAte = Dia.AddDays(21);

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Equal(4, _ambiente.Repositorio.Itens.Count); // original + 3 cópias
        var serie = _ambiente.Repositorio.Itens.Select(a => a.SerieId).Distinct().Single();
        Assert.NotNull(serie);
        Assert.Equal(
            [Dia.AddHours(14), Dia.AddDays(7).AddHours(14), Dia.AddDays(14).AddHours(14), Dia.AddDays(21).AddHours(14)],
            _ambiente.Repositorio.Itens.Select(a => a.Inicio));

        var previa = Assert.Single(_ambiente.Dialogos.Confirmacoes);
        Assert.Equal("Repetir compromisso", previa.Titulo);
        Assert.Contains("Serão criadas 3 cópias, até 01/12/2026.", previa.Mensagem);
        Assert.Equal([true], _fechamentos);
    }

    [Fact]
    public async Task Repetir_PreviaRecusada_MantemOCompromissoEFechaSemGerarCopias()
    {
        var vm = CriarFormulario();
        Preencher(vm);
        vm.Repetir = true;
        vm.RepetirAte = Dia.AddDays(3);
        _ambiente.Dialogos.RespostaConfirmar = false;

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Single(_ambiente.Repositorio.Itens);
        Assert.Equal([true], _fechamentos);
    }

    [Fact]
    public async Task Repetir_PreviaMostraQuantasCopiasConflitam()
    {
        _ambiente.Semear("Médico", Dia.AddDays(1).AddHours(14), Dia.AddDays(1).AddHours(15));
        var vm = CriarFormulario();
        Preencher(vm);
        vm.Repetir = true;
        vm.Frequencia = FrequenciaRepeticao.Diaria;
        vm.RepetirAte = Dia.AddDays(2);

        await vm.SalvarCommand.ExecuteAsync(null);

        var previa = _ambiente.Dialogos.Confirmacoes.Single(c => c.Titulo == "Repetir compromisso").Mensagem;
        Assert.Contains("Serão criadas 2 cópias", previa);
        Assert.Contains("1 delas conflitam", previa);
        Assert.Contains("11/11/2026 14:00", previa);
        Assert.Equal(4, _ambiente.Repositorio.Itens.Count); // Médico + original + 2 cópias
    }

    [Fact]
    public async Task Repetir_ComDuracaoMaiorQueOIntervalo_MostraOErroEMantemOCompromissoSalvo()
    {
        var vm = CriarFormulario();
        vm.Titulo = "Congresso";
        vm.DataInicio = Dia;
        vm.HoraInicio = "08:00";
        vm.DataFim = Dia.AddDays(2); // dura 2 dias
        vm.HoraFim = "18:00";
        vm.Repetir = true;
        vm.Frequencia = FrequenciaRepeticao.Diaria;
        vm.RepetirAte = Dia.AddDays(5);

        await vm.SalvarCommand.ExecuteAsync(null);

        var erro = Assert.Single(_ambiente.Dialogos.Erros);
        Assert.Contains("O compromisso foi salvo, mas a repetição não foi criada.", erro);
        Assert.Contains("as cópias se sobreporiam", erro);
        Assert.Single(_ambiente.Repositorio.Itens);
        Assert.Empty(_ambiente.Dialogos.Confirmacoes);
        Assert.Equal([true], _fechamentos);
    }

    [Fact]
    public async Task Repetir_ComMaisDeUmAno_MostraOErroDoLimite()
    {
        var vm = CriarFormulario();
        Preencher(vm);
        vm.Repetir = true;
        vm.Frequencia = FrequenciaRepeticao.Mensal;
        vm.RepetirAte = Dia.AddYears(2);

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.Contains("no máximo 1 ano", Assert.Single(_ambiente.Dialogos.Erros));
        Assert.Single(_ambiente.Repositorio.Itens);
    }

    [Fact]
    public void UnidadeDoIntervalo_AcompanhaAFrequencia()
    {
        var vm = CriarFormulario();

        vm.Frequencia = FrequenciaRepeticao.Diaria;
        Assert.Equal("dia(s)", vm.UnidadeDoIntervalo);
        vm.Frequencia = FrequenciaRepeticao.Semanal;
        Assert.Equal("semana(s)", vm.UnidadeDoIntervalo);
        vm.Frequencia = FrequenciaRepeticao.Mensal;
        Assert.Equal("mês(es)", vm.UnidadeDoIntervalo);
    }

    [Fact]
    public void Frequencias_OferecemAsTresOpcoes()
    {
        Assert.Equal(
            [FrequenciaRepeticao.Diaria, FrequenciaRepeticao.Semanal, FrequenciaRepeticao.Mensal],
            AgendamentoFormViewModel.Frequencias.Select(f => f.Valor));
    }
}
