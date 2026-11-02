using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
using DateCheck.Domain.Exceptions;

namespace DateCheck.Domain.Tests;

public class AgendamentoTests
{
    private static readonly DateTime Inicio = new(2026, 11, 10, 9, 0, 0);
    private static readonly DateTime Fim = new(2026, 11, 10, 10, 0, 0);

    private static Agendamento Criar() => new("Reunião", Inicio, Fim, "Sala 2");

    [Fact]
    public void Criar_ComDadosValidos_ComecaPendente()
    {
        var agendamento = Criar();

        Assert.Equal("Reunião", agendamento.Titulo);
        Assert.Equal("Sala 2", agendamento.Descricao);
        Assert.Equal(Inicio, agendamento.Inicio);
        Assert.Equal(Fim, agendamento.Fim);
        Assert.Equal(StatusAgendamento.Pendente, agendamento.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_SemTitulo_LancaDomainException(string? titulo)
    {
        Assert.Throws<DomainException>(() => new Agendamento(titulo!, Inicio, Fim));
    }

    [Fact]
    public void Criar_TituloMuitoLongo_LancaDomainException()
    {
        var titulo = new string('a', Agendamento.TamanhoMaximoTitulo + 1);

        Assert.Throws<DomainException>(() => new Agendamento(titulo, Inicio, Fim));
    }

    [Fact]
    public void Criar_AparaEspacosDoTituloEDescricaoVaziaViraNula()
    {
        var agendamento = new Agendamento("  Dentista  ", Inicio, Fim, "   ");

        Assert.Equal("Dentista", agendamento.Titulo);
        Assert.Null(agendamento.Descricao);
    }

    [Fact]
    public void Criar_DescricaoMuitoLonga_LancaDomainException()
    {
        var descricao = new string('a', Agendamento.TamanhoMaximoDescricao + 1);

        Assert.Throws<DomainException>(() => new Agendamento("Título", Inicio, Fim, descricao));
    }

    [Fact]
    public void Criar_FimIgualAoInicio_LancaDomainException()
    {
        Assert.Throws<DomainException>(() => new Agendamento("Título", Inicio, Inicio));
    }

    [Fact]
    public void Criar_FimAnteriorAoInicio_LancaDomainException()
    {
        Assert.Throws<DomainException>(() => new Agendamento("Título", Fim, Inicio));
    }

    [Fact]
    public void Concluir_AgendamentoPendente_MudaStatus()
    {
        var agendamento = Criar();

        agendamento.Concluir();

        Assert.Equal(StatusAgendamento.Concluido, agendamento.Status);
    }

    [Fact]
    public void Concluir_AgendamentoCancelado_LancaDomainException()
    {
        var agendamento = Criar();
        agendamento.Cancelar();

        Assert.Throws<DomainException>(agendamento.Concluir);
    }

    [Fact]
    public void Concluir_DuasVezes_ContinuaConcluido()
    {
        var agendamento = Criar();
        agendamento.Concluir();

        agendamento.Concluir();

        Assert.Equal(StatusAgendamento.Concluido, agendamento.Status);
    }

    [Fact]
    public void Cancelar_AgendamentoPendente_MudaStatus()
    {
        var agendamento = Criar();

        agendamento.Cancelar();

        Assert.Equal(StatusAgendamento.Cancelado, agendamento.Status);
    }

    [Fact]
    public void Cancelar_AgendamentoConcluido_LancaDomainException()
    {
        var agendamento = Criar();
        agendamento.Concluir();

        Assert.Throws<DomainException>(agendamento.Cancelar);
    }

    [Fact]
    public void Cancelar_DuasVezes_ContinuaCancelado()
    {
        var agendamento = Criar();
        agendamento.Cancelar();

        agendamento.Cancelar();

        Assert.Equal(StatusAgendamento.Cancelado, agendamento.Status);
    }

    [Fact]
    public void Reagendar_ComPeriodoValido_AtualizaInicioEFim()
    {
        var agendamento = Criar();
        var novoInicio = Inicio.AddDays(1);
        var novoFim = Fim.AddDays(1);

        agendamento.Reagendar(novoInicio, novoFim);

        Assert.Equal(novoInicio, agendamento.Inicio);
        Assert.Equal(novoFim, agendamento.Fim);
    }

    [Fact]
    public void Reagendar_ComPeriodoInvalido_LancaDomainEMantemOriginal()
    {
        var agendamento = Criar();

        Assert.Throws<DomainException>(() => agendamento.Reagendar(Fim, Inicio));

        Assert.Equal(Inicio, agendamento.Inicio);
        Assert.Equal(Fim, agendamento.Fim);
    }

    [Fact]
    public void Reagendar_AgendamentoCancelado_LancaDomainException()
    {
        var agendamento = Criar();
        agendamento.Cancelar();

        Assert.Throws<DomainException>(() => agendamento.Reagendar(Inicio.AddDays(1), Fim.AddDays(1)));
    }

    [Fact]
    public void Reagendar_AgendamentoConcluido_LancaDomainException()
    {
        var agendamento = Criar();
        agendamento.Concluir();

        Assert.Throws<DomainException>(() => agendamento.Reagendar(Inicio.AddDays(1), Fim.AddDays(1)));
    }

    [Fact]
    public void AlterarDetalhes_ComDadosValidos_AtualizaTituloEDescricao()
    {
        var agendamento = Criar();

        agendamento.AlterarDetalhes("Novo título", null);

        Assert.Equal("Novo título", agendamento.Titulo);
        Assert.Null(agendamento.Descricao);
    }

    [Fact]
    public void AlterarDetalhes_SemTitulo_LancaDomainEMantemOriginal()
    {
        var agendamento = Criar();

        Assert.Throws<DomainException>(() => agendamento.AlterarDetalhes(" ", "x"));

        Assert.Equal("Reunião", agendamento.Titulo);
        Assert.Equal("Sala 2", agendamento.Descricao);
    }

    [Fact]
    public void AlterarDetalhes_AgendamentoConcluido_LancaDomainException()
    {
        var agendamento = Criar();
        agendamento.Concluir();

        Assert.Throws<DomainException>(() => agendamento.AlterarDetalhes("Novo", null));
    }
}
