using DateCheck.Application.Dtos;
using DateCheck.Desktop.ViewModels;
using DateCheck.Domain.Enums;

namespace DateCheck.Desktop.Tests;

public class AgendamentoItemViewModelTests
{
    private static AgendamentoItemViewModel Criar(
        StatusAgendamento status = StatusAgendamento.Pendente,
        bool atrasado = false,
        bool proximo = false,
        string? descricao = null,
        DateTime? inicio = null,
        DateTime? fim = null) =>
        new(new AgendamentoDto(
            7, "Título", descricao,
            inicio ?? new DateTime(2026, 11, 2, 9, 0, 0),
            fim ?? new DateTime(2026, 11, 2, 10, 30, 0),
            status, null, atrasado, proximo));

    [Fact]
    public void Horario_NoMesmoDia_MostraSoAsHoras()
    {
        Assert.Equal("09:00 – 10:30", Criar().Horario);
    }

    [Fact]
    public void Horario_EmVariosDias_MostraDataEHora()
    {
        var item = Criar(inicio: new DateTime(2026, 11, 9, 8, 0, 0), fim: new DateTime(2026, 11, 11, 18, 0, 0));

        Assert.Equal("09/11 08:00 – 11/11 18:00", item.Horario);
    }

    [Theory]
    [InlineData(StatusAgendamento.Pendente, true, true, true, false)]
    [InlineData(StatusAgendamento.Concluido, false, false, false, true)]
    [InlineData(StatusAgendamento.Cancelado, false, false, false, true)]
    public void Acoes_DependemDoStatus(StatusAgendamento status, bool concluir, bool cancelar, bool editar, bool reabrir)
    {
        var item = Criar(status);

        Assert.Equal(concluir, item.PodeConcluir);
        Assert.Equal(cancelar, item.PodeCancelar);
        Assert.Equal(editar, item.PodeEditar);
        Assert.Equal(reabrir, item.PodeReabrir);
    }

    [Theory]
    [InlineData(StatusAgendamento.Pendente, "Pendente")]
    [InlineData(StatusAgendamento.Concluido, "Concluído")]
    [InlineData(StatusAgendamento.Cancelado, "Cancelado")]
    public void StatusTexto_EmPortugues(StatusAgendamento status, string esperado)
    {
        Assert.Equal(esperado, Criar(status).StatusTexto);
    }

    [Fact]
    public void Marcador_ProximoTemPrioridadeSobreAtrasadoENenhumDeixaVazio()
    {
        Assert.Equal("Próximo", Criar(proximo: true, atrasado: true).Marcador);
        Assert.Equal("Atrasado", Criar(atrasado: true).Marcador);
        Assert.Equal(string.Empty, Criar().Marcador);
        Assert.False(Criar().TemMarcador);
        Assert.True(Criar(atrasado: true).TemMarcador);
    }

    [Fact]
    public void Descricao_NulaViraVaziaESemDescricaoNaoMostra()
    {
        Assert.Equal(string.Empty, Criar().Descricao);
        Assert.False(Criar().TemDescricao);
        Assert.True(Criar(descricao: "Sala 2").TemDescricao);
    }

    [Fact]
    public void EstaCancelado_SoParaCancelados()
    {
        Assert.True(Criar(StatusAgendamento.Cancelado).EstaCancelado);
        Assert.False(Criar(StatusAgendamento.Concluido).EstaCancelado);
    }
}
