using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
using DateCheck.Domain.Exceptions;

namespace DateCheck.Domain.Tests;

public class AgendamentoImportadoTests
{
    private static readonly DateTime Inicio = new(2026, 11, 10, 9, 0, 0);
    private static readonly DateTime Fim = new(2026, 11, 10, 10, 0, 0);

    private static Agendamento Importar(
        OrigemAgendamento origem = OrigemAgendamento.Notion,
        string idExterno = "abc-123",
        string? url = "https://www.notion.so/abc123",
        string titulo = "Dentista",
        string? descricao = "Levar exames",
        DateTime? inicio = null,
        DateTime? fim = null,
        bool diaInteiro = false,
        StatusAgendamento status = StatusAgendamento.Pendente) =>
        Agendamento.Importar(origem, idExterno, url, titulo, descricao, inicio ?? Inicio, fim ?? Fim, diaInteiro, status);

    // ---------- Importar ----------

    [Fact]
    public void Importar_PreencheTodosOsCampos()
    {
        var item = Importar(status: StatusAgendamento.Concluido);

        Assert.Equal(OrigemAgendamento.Notion, item.Origem);
        Assert.Equal("abc-123", item.IdExterno);
        Assert.Equal("https://www.notion.so/abc123", item.UrlExterna);
        Assert.Equal("Dentista", item.Titulo);
        Assert.Equal("Levar exames", item.Descricao);
        Assert.Equal(Inicio, item.Inicio);
        Assert.Equal(Fim, item.Fim);
        Assert.False(item.DiaInteiro);
        Assert.Equal(StatusAgendamento.Concluido, item.Status);
        Assert.True(item.EhExterno);
        Assert.Null(item.SerieId);
    }

    [Fact]
    public void AgendamentoLocal_NaoTemIdNemUrlExternosEOrigemEhLocal()
    {
        var local = new Agendamento("Reunião", Inicio, Fim);

        Assert.Equal(OrigemAgendamento.Local, local.Origem);
        Assert.Null(local.IdExterno);
        Assert.Null(local.UrlExterna);
        Assert.False(local.DiaInteiro);
        Assert.False(local.EhExterno);
    }

    [Fact]
    public void Importar_ComOrigemLocal_Falha()
    {
        Assert.Throws<DomainException>(() => Importar(origem: OrigemAgendamento.Local));
    }

    [Fact]
    public void Importar_ComOrigemInexistente_Falha()
    {
        Assert.Throws<DomainException>(() => Importar(origem: (OrigemAgendamento)99));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Importar_SemIdExterno_Falha(string? id)
    {
        var ex = Assert.Throws<DomainException>(() => Importar(idExterno: id!));
        Assert.Contains("identificador externo", ex.Message);
    }

    [Fact]
    public void Importar_IdExternoMuitoLongo_Falha()
    {
        Assert.Throws<DomainException>(() =>
            Importar(idExterno: new string('x', Agendamento.TamanhoMaximoIdExterno + 1)));
    }

    [Fact]
    public void Importar_AparaIdExternoUrlTituloEDescricao()
    {
        var item = Importar(idExterno: "  id-1 ", url: " https://x.test/a ", titulo: "  Título ", descricao: "  desc ");

        Assert.Equal("id-1", item.IdExterno);
        Assert.Equal("https://x.test/a", item.UrlExterna);
        Assert.Equal("Título", item.Titulo);
        Assert.Equal("desc", item.Descricao);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Importar_UrlVaziaViraNula(string? url)
    {
        Assert.Null(Importar(url: url).UrlExterna);
    }

    [Fact]
    public void Importar_UrlMuitoLonga_Falha()
    {
        Assert.Throws<DomainException>(() =>
            Importar(url: "https://x.test/" + new string('a', Agendamento.TamanhoMaximoUrlExterna)));
    }

    [Fact]
    public void Importar_TituloVazioOuMuitoLongo_Falha()
    {
        Assert.Throws<DomainException>(() => Importar(titulo: " "));
        Assert.Throws<DomainException>(() => Importar(titulo: new string('t', Agendamento.TamanhoMaximoTitulo + 1)));
    }

    [Fact]
    public void Importar_DescricaoMuitoLonga_Falha()
    {
        Assert.Throws<DomainException>(() =>
            Importar(descricao: new string('d', Agendamento.TamanhoMaximoDescricao + 1)));
    }

    [Fact]
    public void Importar_FimNaoPosteriorAoInicio_Falha()
    {
        Assert.Throws<DomainException>(() => Importar(fim: Inicio));
        Assert.Throws<DomainException>(() => Importar(fim: Inicio.AddMinutes(-1)));
    }

    [Fact]
    public void Importar_StatusInvalido_Falha()
    {
        Assert.Throws<DomainException>(() => Importar(status: (StatusAgendamento)42));
    }

    // ---------- dia inteiro ----------

    [Fact]
    public void DiaInteiro_UmDia_VaiDaMeiaNoiteAMeiaNoiteSeguinte()
    {
        var item = Importar(
            inicio: new DateTime(2026, 11, 10), fim: new DateTime(2026, 11, 11), diaInteiro: true);

        Assert.True(item.DiaInteiro);
        Assert.Equal(new DateTime(2026, 11, 10), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 11), item.Fim);
    }

    [Fact]
    public void DiaInteiro_VariosDias_EhValido()
    {
        var item = Importar(
            inicio: new DateTime(2026, 11, 10), fim: new DateTime(2026, 11, 14), diaInteiro: true);

        Assert.Equal(new DateTime(2026, 11, 14), item.Fim);
    }

    [Fact]
    public void DiaInteiro_ExigeMeiaNoiteNoInicio()
    {
        Assert.Throws<DomainException>(() => Importar(
            inicio: new DateTime(2026, 11, 10, 8, 0, 0), fim: new DateTime(2026, 11, 11), diaInteiro: true));
    }

    [Fact]
    public void DiaInteiro_ExigeMeiaNoiteNoFim()
    {
        Assert.Throws<DomainException>(() => Importar(
            inicio: new DateTime(2026, 11, 10), fim: new DateTime(2026, 11, 10, 23, 59, 0), diaInteiro: true));
    }

    [Fact]
    public void DiaInteiro_MantemAInvarianteFimMaiorQueInicio()
    {
        Assert.Throws<DomainException>(() => Importar(
            inicio: new DateTime(2026, 11, 10), fim: new DateTime(2026, 11, 10), diaInteiro: true));
    }

    [Fact]
    public void ComHorario_NaoExigeMeiaNoite()
    {
        var item = Importar(diaInteiro: false);

        Assert.False(item.DiaInteiro);
    }

    // ---------- somente leitura ----------

    [Fact]
    public void ItemExterno_NaoPodeSerEditadoPeloDateCheck()
    {
        var item = Importar();
        var acoes = new (string Nome, Action Acao)[]
        {
            (nameof(Agendamento.AlterarDetalhes), () => item.AlterarDetalhes("Novo", null)),
            (nameof(Agendamento.Reagendar), () => item.Reagendar(Inicio.AddDays(1), Fim.AddDays(1))),
            (nameof(Agendamento.Concluir), item.Concluir),
            (nameof(Agendamento.Cancelar), item.Cancelar),
            (nameof(Agendamento.Reabrir), item.Reabrir),
            (nameof(Agendamento.CopiarPara), () => item.CopiarPara(Inicio.AddDays(1))),
            (nameof(Agendamento.AssociarSerie), () => item.AssociarSerie(Guid.NewGuid()))
        };

        foreach (var (nome, acao) in acoes)
        {
            var ex = Assert.Throws<DomainException>(acao);
            Assert.True(ex.Message == "Este compromisso vem do Notion; edite-o lá.", $"{nome}: {ex.Message}");
        }
    }

    [Fact]
    public void ItemExterno_RecusadoMesmoQuandoARepeticaoSeriaUmNoOp()
    {
        var concluido = Importar(status: StatusAgendamento.Concluido);
        var cancelado = Importar(status: StatusAgendamento.Cancelado);

        Assert.Throws<DomainException>(concluido.Concluir);
        Assert.Throws<DomainException>(cancelado.Cancelar);
    }

    [Fact]
    public void ItemExterno_RecusadoNaoAlteraNada()
    {
        var item = Importar();

        Assert.Throws<DomainException>(() => item.AlterarDetalhes("Outro", "x"));
        Assert.Throws<DomainException>(() => item.Reagendar(Inicio.AddDays(1), Fim.AddDays(1)));

        Assert.Equal("Dentista", item.Titulo);
        Assert.Equal(Inicio, item.Inicio);
        Assert.Equal(StatusAgendamento.Pendente, item.Status);
    }

    // ---------- AtualizarDaOrigem ----------

    [Fact]
    public void AtualizarDaOrigem_SubstituiOEstadoMasPreservaOrigemEId()
    {
        var item = Importar();

        item.AtualizarDaOrigem(
            "https://www.notion.so/novo", "Dentista (remarcado)", null,
            new DateTime(2026, 11, 12), new DateTime(2026, 11, 13), true, StatusAgendamento.Concluido);

        Assert.Equal(OrigemAgendamento.Notion, item.Origem);
        Assert.Equal("abc-123", item.IdExterno);
        Assert.Equal("https://www.notion.so/novo", item.UrlExterna);
        Assert.Equal("Dentista (remarcado)", item.Titulo);
        Assert.Null(item.Descricao);
        Assert.Equal(new DateTime(2026, 11, 12), item.Inicio);
        Assert.Equal(new DateTime(2026, 11, 13), item.Fim);
        Assert.True(item.DiaInteiro);
        Assert.Equal(StatusAgendamento.Concluido, item.Status);
    }

    [Fact]
    public void AtualizarDaOrigem_PodeReabrirUmItemConcluido()
    {
        var item = Importar(status: StatusAgendamento.Concluido);

        item.AtualizarDaOrigem(item.UrlExterna, item.Titulo, item.Descricao, item.Inicio, item.Fim, false,
            StatusAgendamento.Pendente);

        Assert.Equal(StatusAgendamento.Pendente, item.Status);
    }

    [Fact]
    public void AtualizarDaOrigem_DadosInvalidos_NaoAlteramNada()
    {
        var item = Importar();

        Assert.Throws<DomainException>(() => item.AtualizarDaOrigem(
            "https://novo.test", "Novo título", "nova", Inicio.AddDays(1), Inicio, false, StatusAgendamento.Cancelado));
        Assert.Throws<DomainException>(() => item.AtualizarDaOrigem(
            "https://novo.test", " ", null, Inicio, Fim, false, StatusAgendamento.Pendente));
        Assert.Throws<DomainException>(() => item.AtualizarDaOrigem(
            "https://novo.test", "Novo", null, Inicio.AddHours(1), Fim, true, StatusAgendamento.Pendente));

        Assert.Equal("Dentista", item.Titulo);
        Assert.Equal("https://www.notion.so/abc123", item.UrlExterna);
        Assert.Equal(Inicio, item.Inicio);
        Assert.False(item.DiaInteiro);
        Assert.Equal(StatusAgendamento.Pendente, item.Status);
    }

    [Fact]
    public void AtualizarDaOrigem_EmItemLocal_Falha()
    {
        var local = new Agendamento("Reunião", Inicio, Fim);

        var ex = Assert.Throws<DomainException>(() => local.AtualizarDaOrigem(
            null, "Outro", null, Inicio, Fim, false, StatusAgendamento.Pendente));

        Assert.Contains("locais", ex.Message);
        Assert.Equal("Reunião", local.Titulo);
    }

    // ---------- itens locais continuam como antes ----------

    [Fact]
    public void ItemLocal_ContinuaEditavel()
    {
        var local = new Agendamento("Reunião", Inicio, Fim);

        local.AlterarDetalhes("Reunião 2", null);
        local.Reagendar(Inicio.AddDays(1), Fim.AddDays(1));
        local.Concluir();
        local.Reabrir();
        var copia = local.CopiarPara(Inicio.AddDays(7), Guid.NewGuid());

        Assert.Equal("Reunião 2", local.Titulo);
        Assert.Equal(OrigemAgendamento.Local, copia.Origem);
        Assert.Null(copia.IdExterno);
    }
}
