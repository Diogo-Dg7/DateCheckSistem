using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;
using DateCheck.Domain.Exceptions;

namespace DateCheck.Domain.Tests;

public class AgendamentoSerieECopiaTests
{
    private static readonly DateTime Inicio = new(2026, 11, 10, 9, 0, 0);
    private static readonly DateTime Fim = new(2026, 11, 10, 10, 30, 0);

    private static Agendamento Criar() => new("Reunião", Inicio, Fim, "Sala 2");

    [Fact]
    public void NovoAgendamento_NaoPertenceAUmaSerie()
    {
        Assert.Null(Criar().SerieId);
    }

    [Fact]
    public void CopiarPara_MantemTituloDescricaoEDuracao()
    {
        var original = Criar();
        var novoInicio = new DateTime(2026, 11, 17, 14, 0, 0);

        var copia = original.CopiarPara(novoInicio);

        Assert.Equal(original.Titulo, copia.Titulo);
        Assert.Equal(original.Descricao, copia.Descricao);
        Assert.Equal(novoInicio, copia.Inicio);
        Assert.Equal(novoInicio.AddMinutes(90), copia.Fim);
    }

    [Fact]
    public void CopiarPara_GeraObjetoNovoEPendenteSemAlterarOOriginal()
    {
        var original = Criar();
        original.Concluir();

        var copia = original.CopiarPara(Inicio.AddDays(7));

        Assert.NotSame(original, copia);
        Assert.Equal(StatusAgendamento.Pendente, copia.Status);
        Assert.Equal(StatusAgendamento.Concluido, original.Status);
        Assert.Equal(Inicio, original.Inicio);
    }

    [Fact]
    public void CopiarPara_DeAgendamentoCancelado_GeraCopiaPendente()
    {
        var original = Criar();
        original.Cancelar();

        Assert.Equal(StatusAgendamento.Pendente, original.CopiarPara(Inicio.AddDays(1)).Status);
    }

    [Fact]
    public void CopiarPara_SemSerie_GeraCopiaAvulsaMesmoSeOriginalTemSerie()
    {
        var original = Criar();
        original.AssociarSerie(Guid.NewGuid());

        Assert.Null(original.CopiarPara(Inicio.AddDays(1)).SerieId);
    }

    [Fact]
    public void CopiarPara_ComSerie_AssociaACopiaASerie()
    {
        var serieId = Guid.NewGuid();

        var copia = Criar().CopiarPara(Inicio.AddDays(1), serieId);

        Assert.Equal(serieId, copia.SerieId);
    }

    [Fact]
    public void AssociarSerie_DefineASerie()
    {
        var agendamento = Criar();
        var serieId = Guid.NewGuid();

        agendamento.AssociarSerie(serieId);

        Assert.Equal(serieId, agendamento.SerieId);
    }

    [Fact]
    public void AssociarSerie_MesmaSerieDuasVezes_NaoLanca()
    {
        var agendamento = Criar();
        var serieId = Guid.NewGuid();
        agendamento.AssociarSerie(serieId);

        agendamento.AssociarSerie(serieId);

        Assert.Equal(serieId, agendamento.SerieId);
    }

    [Fact]
    public void AssociarSerie_OutraSerie_LancaDomainException()
    {
        var agendamento = Criar();
        agendamento.AssociarSerie(Guid.NewGuid());

        Assert.Throws<DomainException>(() => agendamento.AssociarSerie(Guid.NewGuid()));
    }

    [Fact]
    public void AssociarSerie_GuidVazio_LancaDomainException()
    {
        Assert.Throws<DomainException>(() => Criar().AssociarSerie(Guid.Empty));
    }
}
