using Microsoft.EntityFrameworkCore;
using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Infrastructure.Tests;

public class AgendamentoRepositoryTests : TesteDeIntegracaoBase
{
    private static readonly DateTime Dia = new(2026, 11, 10);

    public AgendamentoRepositoryTests(BancoDeDadosFixture banco) : base(banco)
    {
    }

    private static DateTime H(int hora, int minuto = 0, int diasDepois = 0) =>
        Dia.AddDays(diasDepois).AddHours(hora).AddMinutes(minuto);

    private async Task<Agendamento> SalvarAsync(string titulo, DateTime inicio, DateTime fim)
    {
        var agendamento = new Agendamento(titulo, inicio, fim);
        await Repositorio.AdicionarAsync(agendamento);
        return agendamento;
    }

    [Fact]
    public async Task Adicionar_AtribuiIdEPermiteObterComTodosOsCampos()
    {
        var agendamento = new Agendamento("Dentista", H(15, 30), H(16, 15), "Rua das Flores, 10");

        await Repositorio.AdicionarAsync(agendamento);
        var lido = await Repositorio.ObterPorIdAsync(agendamento.Id);

        Assert.True(agendamento.Id > 0);
        Assert.NotNull(lido);
        Assert.NotSame(agendamento, lido);
        Assert.Equal("Dentista", lido.Titulo);
        Assert.Equal("Rua das Flores, 10", lido.Descricao);
        Assert.Equal(H(15, 30), lido.Inicio);
        Assert.Equal(H(16, 15), lido.Fim);
        Assert.Equal(StatusAgendamento.Pendente, lido.Status);
        Assert.Null(lido.SerieId);
    }

    [Fact]
    public async Task Adicionar_TituloEDescricaoNoTamanhoMaximo_Persistem()
    {
        var titulo = new string('t', Agendamento.TamanhoMaximoTitulo);
        var descricao = new string('d', Agendamento.TamanhoMaximoDescricao);
        var agendamento = new Agendamento(titulo, H(9), H(10), descricao);

        await Repositorio.AdicionarAsync(agendamento);
        var lido = await Repositorio.ObterPorIdAsync(agendamento.Id);

        Assert.Equal(titulo, lido!.Titulo);
        Assert.Equal(descricao, lido.Descricao);
    }

    [Fact]
    public async Task Obter_Inexistente_RetornaNulo()
    {
        Assert.Null(await Repositorio.ObterPorIdAsync(987654));
    }

    [Fact]
    public async Task ListarPorPeriodo_RetornaApenasOsQueSeSobrepoemEmOrdemDeInicio()
    {
        await SalvarAsync("Depois", H(14), H(15));
        await SalvarAsync("Dentro", H(10), H(11));
        await SalvarAsync("Muito antes", H(6), H(7));

        var lista = await Repositorio.ListarPorPeriodoAsync(H(9), H(12));

        Assert.Equal(["Dentro"], lista.Select(a => a.Titulo));
    }

    [Fact]
    public async Task ListarPorPeriodo_BordasEncostadasNaoSaoIncluidas()
    {
        await SalvarAsync("Termina quando o período começa", H(8), H(9));
        await SalvarAsync("Começa quando o período termina", H(12), H(13));

        var lista = await Repositorio.ListarPorPeriodoAsync(H(9), H(12));

        Assert.Empty(lista);
    }

    [Fact]
    public async Task ListarPorPeriodo_IncluiQuemAtravessaOInicioOFimOuContemOPeriodo()
    {
        await SalvarAsync("Atravessa o início", H(8), H(9, 1));
        await SalvarAsync("Atravessa o fim", H(11, 59), H(13));
        await SalvarAsync("Contém o período", H(0), H(23));

        var lista = await Repositorio.ListarPorPeriodoAsync(H(9), H(12));

        Assert.Equal(
            ["Contém o período", "Atravessa o início", "Atravessa o fim"],
            lista.Select(a => a.Titulo));
    }

    [Fact]
    public async Task ListarPorPeriodo_IncluiAgendamentoQueAtravessaAMeiaNoite()
    {
        await SalvarAsync("Plantão", H(22, diasDepois: -1), H(6));

        var lista = await Repositorio.ListarPorPeriodoAsync(Dia, Dia.AddDays(1));

        Assert.Single(lista);
    }

    [Fact]
    public async Task ListarPorPeriodo_NaoFiltraPorStatus()
    {
        var cancelado = await SalvarAsync("Cancelado", H(10), H(11));
        cancelado.Cancelar();
        await Repositorio.AtualizarAsync(cancelado);

        var lista = await Repositorio.ListarPorPeriodoAsync(H(9), H(12));

        Assert.Equal(StatusAgendamento.Cancelado, Assert.Single(lista).Status);
    }

    [Fact]
    public async Task Atualizar_PersisteMudancaDeStatus()
    {
        var salvo = await SalvarAsync("Tarefa", H(10), H(11));

        var lido = await Repositorio.ObterPorIdAsync(salvo.Id);
        lido!.Concluir();
        await Repositorio.AtualizarAsync(lido);

        var recarregado = await Repositorio.ObterPorIdAsync(salvo.Id);
        Assert.Equal(StatusAgendamento.Concluido, recarregado!.Status);
    }

    [Fact]
    public async Task Atualizar_PersisteReagendamentoEDetalhes()
    {
        var salvo = await SalvarAsync("Antigo", H(10), H(11));

        var lido = await Repositorio.ObterPorIdAsync(salvo.Id);
        lido!.AlterarDetalhes("Novo", "Com descrição");
        lido.Reagendar(H(15), H(16, 30));
        await Repositorio.AtualizarAsync(lido);

        var recarregado = await Repositorio.ObterPorIdAsync(salvo.Id);
        Assert.Equal("Novo", recarregado!.Titulo);
        Assert.Equal("Com descrição", recarregado.Descricao);
        Assert.Equal(H(15), recarregado.Inicio);
        Assert.Equal(H(16, 30), recarregado.Fim);
    }

    [Fact]
    public async Task Atualizar_NaoDuplicaORegistro()
    {
        var salvo = await SalvarAsync("Tarefa", H(10), H(11));

        var lido = await Repositorio.ObterPorIdAsync(salvo.Id);
        lido!.Cancelar();
        await Repositorio.AtualizarAsync(lido);

        var total = await ComContextoAsync(c => c.Agendamentos.CountAsync());
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task Atualizar_ReabrirVoltaParaPendente()
    {
        var salvo = await SalvarAsync("Tarefa", H(10), H(11));
        salvo.Cancelar();
        await Repositorio.AtualizarAsync(salvo);

        var lido = await Repositorio.ObterPorIdAsync(salvo.Id);
        lido!.Reabrir();
        await Repositorio.AtualizarAsync(lido);

        Assert.Equal(StatusAgendamento.Pendente, (await Repositorio.ObterPorIdAsync(salvo.Id))!.Status);
    }

    [Fact]
    public async Task AdicionarSerie_GravaOriginalECopiasComOMesmoSerieId()
    {
        var original = await SalvarAsync("Academia", H(7), H(8));
        var lido = await Repositorio.ObterPorIdAsync(original.Id);
        var serieId = Guid.NewGuid();
        lido!.AssociarSerie(serieId);
        var copias = new[]
        {
            lido.CopiarPara(H(7, diasDepois: 1), serieId),
            lido.CopiarPara(H(7, diasDepois: 2), serieId),
            lido.CopiarPara(H(7, diasDepois: 3), serieId)
        };

        await Repositorio.AdicionarSerieAsync(lido, copias);

        Assert.All(copias, c => Assert.True(c.Id > 0));
        var todos = await ComContextoAsync(c => c.Agendamentos.AsNoTracking().OrderBy(a => a.Inicio).ToListAsync());
        Assert.Equal(4, todos.Count);
        Assert.All(todos, a => Assert.Equal(serieId, a.SerieId));
        Assert.Equal(original.Id, todos[0].Id);
        Assert.Equal(4, todos.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public async Task AdicionarSerie_NaoDuplicaOOriginalNemAlteraSeusDados()
    {
        var original = await SalvarAsync("Academia", H(7), H(8));
        var lido = await Repositorio.ObterPorIdAsync(original.Id);
        var serieId = Guid.NewGuid();
        lido!.AssociarSerie(serieId);

        await Repositorio.AdicionarSerieAsync(lido, [lido.CopiarPara(H(7, diasDepois: 1), serieId)]);

        var recarregado = await Repositorio.ObterPorIdAsync(original.Id);
        Assert.Equal("Academia", recarregado!.Titulo);
        Assert.Equal(H(7), recarregado.Inicio);
        Assert.Equal(2, await ComContextoAsync(c => c.Agendamentos.CountAsync()));
    }

    [Fact]
    public async Task Copias_DeSerieNaoAfetamAgendamentosAvulsos()
    {
        var avulso = await SalvarAsync("Avulso", H(20), H(21));
        var original = await SalvarAsync("Academia", H(7), H(8));
        var lido = await Repositorio.ObterPorIdAsync(original.Id);
        var serieId = Guid.NewGuid();
        lido!.AssociarSerie(serieId);

        await Repositorio.AdicionarSerieAsync(lido, [lido.CopiarPara(H(7, diasDepois: 1), serieId)]);

        Assert.Null((await Repositorio.ObterPorIdAsync(avulso.Id))!.SerieId);
    }
}
