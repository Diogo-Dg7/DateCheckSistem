using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DateCheck.Infrastructure.Tests;

public class OrigemExternaRepositoryTests : TesteDeIntegracaoBase
{
    private static readonly DateTime Dia = new(2026, 11, 10);
    private static readonly DateTime JanelaInicio = Dia.AddDays(-90);
    private static readonly DateTime JanelaFim = Dia.AddDays(365);

    public OrigemExternaRepositoryTests(BancoDeDadosFixture banco) : base(banco)
    {
    }

    private static DateTime H(int hora, int diasDepois = 0) => Dia.AddDays(diasDepois).AddHours(hora);

    private static Agendamento Notion(
        string id,
        string titulo = "Item",
        DateTime? inicio = null,
        DateTime? fim = null,
        bool diaInteiro = false,
        StatusAgendamento status = StatusAgendamento.Pendente,
        string? url = null) =>
        Agendamento.Importar(
            OrigemAgendamento.Notion, id, url ?? $"https://www.notion.so/{id}", titulo, null,
            inicio ?? H(9), fim ?? (inicio ?? H(9)).AddHours(1), diaInteiro, status);

    private Task<Agendamento> SalvarNotionAsync(string id, string titulo = "Item", DateTime? inicio = null) =>
        SalvarAsync(Notion(id, titulo, inicio));

    private async Task<Agendamento> SalvarAsync(Agendamento agendamento)
    {
        await Repositorio.AdicionarAsync(agendamento);
        return agendamento;
    }

    private Task<List<Agendamento>> TodosAsync() =>
        ComContextoAsync(c => c.Agendamentos.AsNoTracking().OrderBy(a => a.Id).ToListAsync());

    private Task<ResultadoSync> SincronizarAsync(params Agendamento[] recebidos) =>
        SincronizarNaJanelaAsync(JanelaInicio, JanelaFim, recebidos);

    private async Task<ResultadoSync> SincronizarNaJanelaAsync(DateTime inicio, DateTime fim, params Agendamento[] recebidos)
    {
        var r = await Repositorio.SincronizarAsync(OrigemAgendamento.Notion, inicio, fim, recebidos);
        return new ResultadoSync(r.Inseridos, r.Atualizados, r.Removidos);
    }

    private sealed record ResultadoSync(int Inseridos, int Atualizados, int Removidos);

    // ---------- mapeamento ----------

    [Fact]
    public async Task ItemExterno_PersisteTodosOsCamposNovos()
    {
        var item = Agendamento.Importar(
            OrigemAgendamento.Notion, new string('i', Agendamento.TamanhoMaximoIdExterno),
            "https://www.notion.so/" + new string('u', Agendamento.TamanhoMaximoUrlExterna - 23),
            "Férias", "Praia", Dia, Dia.AddDays(5), true, StatusAgendamento.Concluido);

        await Repositorio.AdicionarAsync(item);
        var lido = await Repositorio.ObterPorIdAsync(item.Id);

        Assert.NotNull(lido);
        Assert.Equal(OrigemAgendamento.Notion, lido.Origem);
        Assert.Equal(item.IdExterno, lido.IdExterno);
        Assert.Equal(item.UrlExterna, lido.UrlExterna);
        Assert.True(lido.DiaInteiro);
        Assert.Equal(Dia.AddDays(5), lido.Fim);
        Assert.Equal(StatusAgendamento.Concluido, lido.Status);
    }

    [Fact]
    public async Task ItemLocal_GravaOrigemLocalSemIdExterno()
    {
        var local = await SalvarAsync(new Agendamento("Reunião", H(9), H(10)));

        var lido = await Repositorio.ObterPorIdAsync(local.Id);

        Assert.Equal(OrigemAgendamento.Local, lido!.Origem);
        Assert.Null(lido.IdExterno);
        Assert.Null(lido.UrlExterna);
        Assert.False(lido.DiaInteiro);
    }

    [Fact]
    public async Task IndiceUnico_RecusaMesmoIdExternoNaMesmaOrigem()
    {
        await SalvarNotionAsync("pagina-1");

        await Assert.ThrowsAsync<DbUpdateException>(() => SalvarNotionAsync("pagina-1", "Outro"));
    }

    [Fact]
    public async Task IndiceUnico_NaoRestringeItensLocaisSemIdExterno()
    {
        await SalvarAsync(new Agendamento("A", H(9), H(10)));
        await SalvarAsync(new Agendamento("B", H(9), H(10)));
        await SalvarAsync(new Agendamento("C", H(9), H(10)));

        Assert.Equal(3, (await TodosAsync()).Count);
    }

    // ---------- ListarPorOrigemAsync ----------

    [Fact]
    public async Task ListarPorOrigem_FiltraPelaOrigemEPelaJanelaDeInicio()
    {
        await SalvarAsync(new Agendamento("Local", H(9), H(10)));
        await SalvarNotionAsync("dentro-b", "Dentro B", H(15));
        await SalvarNotionAsync("dentro-a", "Dentro A", H(8));
        await SalvarNotionAsync("antes", "Antes", H(9, diasDepois: -1));
        await SalvarNotionAsync("depois", "Depois", H(9, diasDepois: 1));

        var lista = await Repositorio.ListarPorOrigemAsync(OrigemAgendamento.Notion, Dia, Dia.AddDays(1));

        Assert.Equal(["Dentro A", "Dentro B"], lista.Select(a => a.Titulo));
    }

    [Fact]
    public async Task ListarPorOrigem_JanelaIncluiOInicioEExcluiOFim()
    {
        await SalvarNotionAsync("no-inicio", inicio: Dia);
        await SalvarNotionAsync("no-fim", inicio: Dia.AddDays(1));

        var lista = await Repositorio.ListarPorOrigemAsync(OrigemAgendamento.Notion, Dia, Dia.AddDays(1));

        Assert.Equal(["no-inicio"], lista.Select(a => a.IdExterno));
    }

    // ---------- SincronizarAsync ----------

    [Fact]
    public async Task Sincronizar_ItemNovo_Insere()
    {
        var resultado = await SincronizarAsync(Notion("novo", "Novo"));

        Assert.Equal(new ResultadoSync(1, 0, 0), resultado);
        var salvo = Assert.Single(await TodosAsync());
        Assert.Equal("novo", salvo.IdExterno);
        Assert.Equal("Novo", salvo.Titulo);
    }

    [Fact]
    public async Task Sincronizar_ItemJaImportado_AtualizaSemDuplicarEMantemOId()
    {
        var existente = await SalvarNotionAsync("p1", "Antes", H(9));

        var resultado = await SincronizarAsync(Notion("p1", "Depois", H(14), status: StatusAgendamento.Concluido));

        Assert.Equal(new ResultadoSync(0, 1, 0), resultado);
        var salvo = Assert.Single(await TodosAsync());
        Assert.Equal(existente.Id, salvo.Id);
        Assert.Equal("Depois", salvo.Titulo);
        Assert.Equal(H(14), salvo.Inicio);
        Assert.Equal(StatusAgendamento.Concluido, salvo.Status);
    }

    [Fact]
    public async Task Sincronizar_ItemIdentico_NaoContaComoAtualizado()
    {
        await Repositorio.SincronizarAsync(OrigemAgendamento.Notion, JanelaInicio, JanelaFim, [Notion("p1", "Igual")]);

        var resultado = await SincronizarAsync(Notion("p1", "Igual"));

        Assert.Equal(new ResultadoSync(0, 0, 0), resultado);
    }

    [Fact]
    public async Task Sincronizar_ItemImportadoQueNaoVeioMais_DentroDaJanela_ERemovido()
    {
        await SalvarNotionAsync("some", "Some", H(9));
        await SalvarNotionAsync("fica", "Fica", H(10));

        var resultado = await SincronizarAsync(Notion("fica", "Fica", H(10)));

        Assert.Equal(new ResultadoSync(0, 0, 1), resultado);
        Assert.Equal(["fica"], (await TodosAsync()).Select(a => a.IdExterno));
    }

    [Fact]
    public async Task Sincronizar_NuncaRemoveItemForaDaJanelaConsultada()
    {
        await SalvarNotionAsync("antigo", "Antigo", JanelaInicio.AddDays(-1));
        await SalvarNotionAsync("distante", "Distante", JanelaFim.AddDays(1));
        await SalvarNotionAsync("na-borda", "Na borda", JanelaFim); // o fim da janela é exclusivo

        var resultado = await SincronizarAsync();

        Assert.Equal(new ResultadoSync(0, 0, 0), resultado);
        Assert.Equal(3, (await TodosAsync()).Count);
    }

    [Fact]
    public async Task Sincronizar_RemoveItemNaBordaInicialDaJanela()
    {
        await SalvarNotionAsync("na-borda", "Na borda", JanelaInicio);

        var resultado = await SincronizarAsync();

        Assert.Equal(1, resultado.Removidos);
        Assert.Empty(await TodosAsync());
    }

    [Fact]
    public async Task Sincronizar_ItemRecebidoQueJaExistiaForaDaJanela_AtualizaEmVezDeDuplicar()
    {
        // O item estava com a data antiga (fora da janela) e agora foi movido para dentro dela.
        await SalvarNotionAsync("movido", "Movido", JanelaInicio.AddDays(-30));

        var resultado = await SincronizarAsync(Notion("movido", "Movido", H(9)));

        Assert.Equal(new ResultadoSync(0, 1, 0), resultado);
        var salvo = Assert.Single(await TodosAsync());
        Assert.Equal(H(9), salvo.Inicio);
    }

    [Fact]
    public async Task Sincronizar_NuncaTocaEmItensLocais()
    {
        var local = await SalvarAsync(new Agendamento("Meu compromisso", H(9), H(10)));
        await SalvarNotionAsync("p1", "Notion", H(9));

        await SincronizarAsync();

        var restante = Assert.Single(await TodosAsync());
        Assert.Equal(local.Id, restante.Id);
        Assert.Equal(OrigemAgendamento.Local, restante.Origem);
    }

    [Fact]
    public async Task Sincronizar_FazInserirAtualizarERemoverJuntos()
    {
        await SalvarNotionAsync("atualiza", "Antes", H(9));
        await SalvarNotionAsync("remove", "Remove", H(11));

        var resultado = await SincronizarAsync(
            Notion("atualiza", "Depois", H(9)),
            Notion("insere", "Insere", H(13)));

        Assert.Equal(new ResultadoSync(1, 1, 1), resultado);
        var titulos = (await TodosAsync()).Select(a => a.Titulo).Order().ToList();
        Assert.Equal(["Depois", "Insere"], titulos);
    }

    [Fact]
    public async Task Sincronizar_AceitaItensDeDiaInteiro()
    {
        await SincronizarAsync(Notion("ferias", "Férias", Dia, Dia.AddDays(3), diaInteiro: true));

        var salvo = Assert.Single(await TodosAsync());
        Assert.True(salvo.DiaInteiro);
        Assert.Equal(Dia.AddDays(3), salvo.Fim);
    }

    [Fact]
    public async Task Sincronizar_ComFalhaNoBanco_DesfazTudo()
    {
        await SalvarNotionAsync("atualiza", "Antes", H(9));
        await SalvarNotionAsync("remove", "Remove", H(11));

        // Uma restrição só deste teste faz o último INSERT falhar, depois que o UPDATE e o DELETE já foram enviados.
        await ComContextoAsync(c => c.Database.ExecuteSqlRawAsync(
            "ALTER TABLE dbo.Agendamentos ADD CONSTRAINT CK_Teste_Atomicidade CHECK (Titulo <> N'EXPLODE')"));
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => SincronizarAsync(
                Notion("atualiza", "Depois", H(9)),
                Notion("explode", "EXPLODE", H(13))));
        }
        finally
        {
            await ComContextoAsync(c => c.Database.ExecuteSqlRawAsync(
                "ALTER TABLE dbo.Agendamentos DROP CONSTRAINT CK_Teste_Atomicidade"));
        }

        var itens = await TodosAsync();
        Assert.Equal(["Antes", "Remove"], itens.Select(a => a.Titulo).Order());
    }

    [Fact]
    public async Task Sincronizar_OrigemLocal_EhRecusadaENaoApagaNada()
    {
        await SalvarAsync(new Agendamento("Local", H(9), H(10)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Repositorio.SincronizarAsync(OrigemAgendamento.Local, JanelaInicio, JanelaFim, []));

        Assert.Single(await TodosAsync());
    }

    [Fact]
    public async Task Sincronizar_ItemDeOutraOrigemOuIdRepetido_EhRecusadoSemAlterarNada()
    {
        await SalvarNotionAsync("p1", "Existente", H(9));

        var local = new Agendamento("Local", H(9), H(10));
        await Assert.ThrowsAsync<ArgumentException>(() => SincronizarAsync(local));
        await Assert.ThrowsAsync<ArgumentException>(() => SincronizarAsync(Notion("dup"), Notion("DUP")));

        Assert.Equal(["p1"], (await TodosAsync()).Select(a => a.IdExterno));
    }
}
