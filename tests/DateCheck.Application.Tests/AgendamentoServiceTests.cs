using DateCheck.Application.Dtos;
using DateCheck.Application.Excecoes;
using DateCheck.Application.Servicos;
using DateCheck.Application.Tests.Fakes;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
using DateCheck.Domain.Exceptions;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Application.Tests;

public class AgendamentoServiceTests
{
    private static readonly DateTime Hoje = new(2026, 11, 10);

    private readonly FakeAgendamentoRepository _repositorio = new();
    private readonly FakeTimeProvider _relogio = new();
    private readonly AgendamentoService _servico;

    public AgendamentoServiceTests()
    {
        _relogio.SetLocalTimeZone(TimeZoneInfo.Utc);
        DefinirAgora(Hoje.AddHours(12));
        _servico = new AgendamentoService(_repositorio, _relogio);
    }

    private void DefinirAgora(DateTime agora) =>
        _relogio.SetUtcNow(new DateTimeOffset(DateTime.SpecifyKind(agora, DateTimeKind.Utc)));

    private static DateTime H(int hora, int minuto = 0, int diasDepois = 0) =>
        Hoje.AddDays(diasDepois).AddHours(hora).AddMinutes(minuto);

    private Agendamento Semear(string titulo, DateTime inicio, DateTime fim) =>
        _repositorio.Semear(new Agendamento(titulo, inicio, fim));

    [Fact]
    public async Task Criar_SalvaERetornaDtoPendente()
    {
        var dto = await _servico.CriarAsync(new NovoAgendamentoRequest("Dentista", H(15), H(16), "Rua A"));

        Assert.True(dto.Id > 0);
        Assert.Equal(StatusAgendamento.Pendente, dto.Status);
        Assert.Equal("Rua A", dto.Descricao);
        Assert.Single(_repositorio.Itens);
    }

    [Fact]
    public async Task Criar_ComPeriodoInvalido_LancaDomainExceptionENaoSalva()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            _servico.CriarAsync(new NovoAgendamentoRequest("Título", H(16), H(15))));

        Assert.Empty(_repositorio.Itens);
    }

    [Fact]
    public async Task Obter_Inexistente_LancaAgendamentoNaoEncontrado()
    {
        await Assert.ThrowsAsync<AgendamentoNaoEncontradoException>(() => _servico.ObterAsync(99));
    }

    [Fact]
    public async Task Editar_AtualizaDetalhesEPeriodo()
    {
        var existente = Semear("Antigo", H(9), H(10));

        var dto = await _servico.EditarAsync(
            new EditarAgendamentoRequest(existente.Id, "Novo", H(11), H(12), "Detalhe"));

        Assert.Equal("Novo", dto.Titulo);
        Assert.Equal(H(11), existente.Inicio);
        Assert.Equal("Detalhe", existente.Descricao);
        Assert.Equal(1, _repositorio.Gravacoes);
    }

    [Fact]
    public async Task Editar_AgendamentoCancelado_LancaDomainExceptionENaoSalva()
    {
        var existente = Semear("Antigo", H(9), H(10));
        existente.Cancelar();

        await Assert.ThrowsAsync<DomainException>(() =>
            _servico.EditarAsync(new EditarAgendamentoRequest(existente.Id, "Novo", H(11), H(12))));

        Assert.Equal(0, _repositorio.Gravacoes);
    }

    [Fact]
    public async Task Editar_Inexistente_LancaAgendamentoNaoEncontrado()
    {
        await Assert.ThrowsAsync<AgendamentoNaoEncontradoException>(() =>
            _servico.EditarAsync(new EditarAgendamentoRequest(99, "Novo", H(11), H(12))));
    }

    [Fact]
    public async Task Concluir_MudaStatusESalva()
    {
        var existente = Semear("Tarefa", H(9), H(10));

        var dto = await _servico.ConcluirAsync(existente.Id);

        Assert.Equal(StatusAgendamento.Concluido, dto.Status);
        Assert.Equal(1, _repositorio.Gravacoes);
    }

    [Fact]
    public async Task Concluir_Cancelado_LancaDomainException()
    {
        var existente = Semear("Tarefa", H(9), H(10));
        existente.Cancelar();

        await Assert.ThrowsAsync<DomainException>(() => _servico.ConcluirAsync(existente.Id));
    }

    [Fact]
    public async Task Cancelar_MudaStatusESalva()
    {
        var existente = Semear("Tarefa", H(9), H(10));

        var dto = await _servico.CancelarAsync(existente.Id);

        Assert.Equal(StatusAgendamento.Cancelado, dto.Status);
    }

    [Fact]
    public async Task Reabrir_VoltaParaPendente()
    {
        var existente = Semear("Tarefa", H(9), H(10));
        existente.Cancelar();

        var dto = await _servico.ReabrirAsync(existente.Id);

        Assert.Equal(StatusAgendamento.Pendente, dto.Status);
    }

    [Fact]
    public async Task ListarDoDia_RetornaSoOsDoDiaEmOrdemDeHorario()
    {
        Semear("Tarde", H(15), H(16));
        Semear("Manhã", H(8), H(9));
        Semear("Amanhã", H(8, diasDepois: 1), H(9, diasDepois: 1));
        Semear("Ontem", H(8, diasDepois: -1), H(9, diasDepois: -1));

        var lista = await _servico.ListarDoDiaAsync();

        Assert.Equal(["Manhã", "Tarde"], lista.Select(a => a.Titulo));
    }

    [Fact]
    public async Task ListarDoDia_IncluiAgendamentoQueVemDaNoiteAnterior()
    {
        Semear("Plantão", H(22, diasDepois: -1), H(6));

        var lista = await _servico.ListarDoDiaAsync();

        Assert.Single(lista);
    }

    [Fact]
    public async Task ListarDoDia_MarcaPendentesQueJaTerminaramComoAtrasados()
    {
        Semear("Atrasado", H(8), H(9));
        Semear("Futuro", H(15), H(16));
        var concluido = Semear("Concluído", H(7), H(8));
        concluido.Concluir();

        var lista = await _servico.ListarDoDiaAsync();

        Assert.True(lista.Single(a => a.Titulo == "Atrasado").Atrasado);
        Assert.False(lista.Single(a => a.Titulo == "Futuro").Atrasado);
        Assert.False(lista.Single(a => a.Titulo == "Concluído").Atrasado);
    }

    [Fact]
    public async Task ListarDoDia_MarcaOProximoPendenteQueAindaNaoTerminou()
    {
        Semear("Atrasado", H(8), H(9));
        Semear("Segundo", H(17), H(18));
        Semear("Próximo", H(15), H(16));

        var lista = await _servico.ListarDoDiaAsync();

        Assert.Equal(["Próximo"], lista.Where(a => a.Proximo).Select(a => a.Titulo));
    }

    [Fact]
    public async Task ListarDoDia_AgendamentoEmAndamentoContaComoProximo()
    {
        Semear("Em andamento", H(11, 30), H(12, 30));
        Semear("Depois", H(15), H(16));

        var lista = await _servico.ListarDoDiaAsync();

        Assert.Equal(["Em andamento"], lista.Where(a => a.Proximo).Select(a => a.Titulo));
    }

    [Fact]
    public async Task ListarDoDia_CanceladoNaoEhProximo()
    {
        var cancelado = Semear("Cancelado", H(15), H(16));
        cancelado.Cancelar();

        var lista = await _servico.ListarDoDiaAsync();

        Assert.DoesNotContain(lista, a => a.Proximo);
    }

    [Fact]
    public async Task ListarDoDia_DeOutroDia_NaoMarcaProximo()
    {
        Semear("Amanhã", H(8, diasDepois: 1), H(9, diasDepois: 1));

        var lista = await _servico.ListarDoDiaAsync(Hoje.AddDays(1));

        Assert.Single(lista);
        Assert.DoesNotContain(lista, a => a.Proximo);
    }

    [Fact]
    public async Task ListarDoMes_RetornaSoOsAgendamentosDoMesEmOrdem()
    {
        Semear("Fim de novembro", new DateTime(2026, 11, 30, 9, 0, 0), new DateTime(2026, 11, 30, 10, 0, 0));
        Semear("Início de novembro", new DateTime(2026, 11, 1, 9, 0, 0), new DateTime(2026, 11, 1, 10, 0, 0));
        Semear("Outubro", new DateTime(2026, 10, 31, 9, 0, 0), new DateTime(2026, 10, 31, 10, 0, 0));
        Semear("Dezembro", new DateTime(2026, 12, 1, 9, 0, 0), new DateTime(2026, 12, 1, 10, 0, 0));

        var lista = await _servico.ListarDoMesAsync(2026, 11);

        Assert.Equal(["Início de novembro", "Fim de novembro"], lista.Select(a => a.Titulo));
    }

    [Fact]
    public async Task ListarPeriodo_RetornaOsQueSeSobrepoemEmOrdemSemIncluirBordasEncostadas()
    {
        Semear("Antes", H(6), H(8));
        Semear("Encostado no início", H(7), H(9));
        Semear("Dentro", H(10), H(11));
        Semear("Atravessa o fim", H(11, 30), H(13));
        Semear("Encostado no fim", H(12), H(13));

        var lista = await _servico.ListarPeriodoAsync(H(9), H(12));

        Assert.Equal(["Dentro", "Atravessa o fim"], lista.Select(a => a.Titulo));
    }

    [Fact]
    public async Task ListarDoMes_DezembroNaoVazaParaJaneiro()
    {
        Semear("Dezembro", new DateTime(2026, 12, 31, 9, 0, 0), new DateTime(2026, 12, 31, 10, 0, 0));
        Semear("Janeiro", new DateTime(2027, 1, 1, 9, 0, 0), new DateTime(2027, 1, 1, 10, 0, 0));

        var lista = await _servico.ListarDoMesAsync(2026, 12);

        Assert.Equal(["Dezembro"], lista.Select(a => a.Titulo));
    }
}
