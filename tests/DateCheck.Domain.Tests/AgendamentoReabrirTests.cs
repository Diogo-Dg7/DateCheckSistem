using DateCheck.Domain.Entidades;
using DateCheck.Domain.Enums;

namespace DateCheck.Domain.Tests;

public class AgendamentoReabrirTests
{
    private static Agendamento Criar() =>
        new("Reunião", new DateTime(2026, 11, 10, 9, 0, 0), new DateTime(2026, 11, 10, 10, 0, 0));

    [Fact]
    public void Reabrir_AgendamentoConcluido_VoltaParaPendente()
    {
        var agendamento = Criar();
        agendamento.Concluir();

        agendamento.Reabrir();

        Assert.Equal(StatusAgendamento.Pendente, agendamento.Status);
    }

    [Fact]
    public void Reabrir_AgendamentoCancelado_VoltaParaPendente()
    {
        var agendamento = Criar();
        agendamento.Cancelar();

        agendamento.Reabrir();

        Assert.Equal(StatusAgendamento.Pendente, agendamento.Status);
    }

    [Fact]
    public void Reabrir_AgendamentoPendente_ContinuaPendente()
    {
        var agendamento = Criar();

        agendamento.Reabrir();

        Assert.Equal(StatusAgendamento.Pendente, agendamento.Status);
    }

    [Fact]
    public void Reabrir_PermiteReagendarNovamente()
    {
        var agendamento = Criar();
        agendamento.Cancelar();
        agendamento.Reabrir();

        agendamento.Reagendar(new DateTime(2026, 11, 11, 9, 0, 0), new DateTime(2026, 11, 11, 10, 0, 0));

        Assert.Equal(new DateTime(2026, 11, 11, 9, 0, 0), agendamento.Inicio);
    }
}
