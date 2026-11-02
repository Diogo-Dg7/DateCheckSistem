using DateCheck.Application.Dtos;
using DateCheck.Desktop.ViewModels;
using DateCheck.Domain.Enums;

namespace DateCheck.Desktop.Tests;

public class GradeDoMesTests
{
    private static AgendamentoDto Dto(string titulo, DateTime inicio, DateTime fim,
        StatusAgendamento status = StatusAgendamento.Pendente) =>
        new(1, titulo, null, inicio, fim, status, null);

    [Fact]
    public void Dias_SempreTemSeisSemanasCompletas()
    {
        Assert.Equal(42, GradeDoMes.Dias(2026, 11).Count);
        Assert.Equal(GradeDoMes.TotalDeDias, GradeDoMes.Dias(2026, 2).Count);
    }

    [Fact]
    public void Dias_MesQueComecaNoDomingo_ComecaNoDiaPrimeiro()
    {
        var dias = GradeDoMes.Dias(2026, 11); // 01/11/2026 é domingo

        Assert.Equal(new DateTime(2026, 11, 1), dias[0]);
        Assert.Equal(new DateTime(2026, 12, 12), dias[^1]);
    }

    [Fact]
    public void Dias_MesQueComecaNoMeioDaSemana_ComecaNoDomingoAnterior()
    {
        var dias = GradeDoMes.Dias(2025, 1); // 01/01/2025 é quarta-feira

        Assert.Equal(new DateTime(2024, 12, 29), dias[0]);
        Assert.Equal(DayOfWeek.Sunday, dias[0].DayOfWeek);
    }

    [Fact]
    public void Dias_MesQueComecaNoSabado_PrecisaDaSextaSemana()
    {
        var dias = GradeDoMes.Dias(2026, 8); // 01/08/2026 é sábado; 31 dias

        Assert.Equal(new DateTime(2026, 7, 26), dias[0]);
        Assert.Contains(new DateTime(2026, 8, 31), dias.Skip(35)); // o último dia cai na 6ª semana
    }

    [Fact]
    public void Dias_FevereiroDeAnoBissextoETerminaIncluido()
    {
        var dias = GradeDoMes.Dias(2028, 2);

        Assert.Contains(new DateTime(2028, 2, 29), dias);
    }

    [Theory]
    [InlineData(2026, 1)]
    [InlineData(2026, 2)]
    [InlineData(2026, 5)]
    [InlineData(2026, 8)]
    [InlineData(2026, 12)]
    [InlineData(2027, 1)]
    [InlineData(2028, 2)]
    public void Dias_SaoConsecutivosComecamNoDomingoEContemOMesInteiro(int ano, int mes)
    {
        var dias = GradeDoMes.Dias(ano, mes);

        Assert.Equal(DayOfWeek.Sunday, dias[0].DayOfWeek);
        for (var i = 1; i < dias.Count; i++)
            Assert.Equal(dias[i - 1].AddDays(1), dias[i]);

        for (var dia = 1; dia <= DateTime.DaysInMonth(ano, mes); dia++)
            Assert.Contains(new DateTime(ano, mes, dia), dias);
    }

    [Fact]
    public void Dias_ViradaDeAno_FuncionaParaDezembro()
    {
        var dias = GradeDoMes.Dias(2026, 12);

        Assert.Contains(new DateTime(2027, 1, 9), dias);
    }

    [Fact]
    public void Montar_MarcaDiasDoMesAtualEHoje()
    {
        var dias = GradeDoMes.Montar(2026, 11, new DateTime(2026, 11, 2, 13, 30, 0), []);

        Assert.Equal(30, dias.Count(d => d.DoMesAtual));
        Assert.Equal(new DateTime(2026, 11, 2), Assert.Single(dias, d => d.EhHoje).Data);
        Assert.All(dias.Where(d => d.Data.Month == 12), d => Assert.False(d.DoMesAtual));
    }

    [Fact]
    public void Montar_HojeForaDoMes_NaoMarcaNenhumDiaComoHoje()
    {
        var dias = GradeDoMes.Montar(2026, 9, new DateTime(2026, 11, 2), []);

        Assert.DoesNotContain(dias, d => d.EhHoje);
    }

    [Fact]
    public void Montar_PoeCadaAgendamentoNoSeuDiaComCorDoStatus()
    {
        var dias = GradeDoMes.Montar(2026, 11, new DateTime(2026, 11, 2),
        [
            Dto("Consulta", new DateTime(2026, 11, 4, 9, 0, 0), new DateTime(2026, 11, 4, 10, 0, 0)),
            Dto("Feito", new DateTime(2026, 11, 4, 11, 0, 0), new DateTime(2026, 11, 4, 12, 0, 0), StatusAgendamento.Concluido)
        ]);

        var dia4 = dias.Single(d => d.Data == new DateTime(2026, 11, 4));
        Assert.Equal(["09:00 Consulta", "11:00 Feito"], dia4.Chips.Select(c => c.Texto));
        Assert.Equal([StatusAgendamento.Pendente, StatusAgendamento.Concluido], dia4.Chips.Select(c => c.Status));
        Assert.Equal(0, dias.Single(d => d.Data == new DateTime(2026, 11, 3)).Total);
    }

    [Fact]
    public void Montar_AgendamentoDeVariosDiasApareceEmTodosElesComoContinuacao()
    {
        var dias = GradeDoMes.Montar(2026, 11, new DateTime(2026, 11, 2),
        [
            Dto("Viagem", new DateTime(2026, 11, 9, 8, 0, 0), new DateTime(2026, 11, 11, 18, 0, 0))
        ]);

        Assert.Equal(["08:00 Viagem"], dias.Single(d => d.Data == new DateTime(2026, 11, 9)).Chips.Select(c => c.Texto));
        Assert.Equal(["↳ Viagem"], dias.Single(d => d.Data == new DateTime(2026, 11, 10)).Chips.Select(c => c.Texto));
        Assert.Equal(["↳ Viagem"], dias.Single(d => d.Data == new DateTime(2026, 11, 11)).Chips.Select(c => c.Texto));
        Assert.Equal(0, dias.Single(d => d.Data == new DateTime(2026, 11, 12)).Total);
        Assert.Equal(0, dias.Single(d => d.Data == new DateTime(2026, 11, 8)).Total);
    }

    [Fact]
    public void Montar_AgendamentoQueTerminaExatamenteNaMeiaNoite_NaoApareceNoDiaSeguinte()
    {
        var dias = GradeDoMes.Montar(2026, 11, new DateTime(2026, 11, 2),
        [
            Dto("Festa", new DateTime(2026, 11, 7, 20, 0, 0), new DateTime(2026, 11, 8, 0, 0, 0))
        ]);

        Assert.Equal(1, dias.Single(d => d.Data == new DateTime(2026, 11, 7)).Total);
        Assert.Equal(0, dias.Single(d => d.Data == new DateTime(2026, 11, 8)).Total);
    }

    [Fact]
    public void Montar_MaisDoQueOMaximo_MostraOsPrimeirosEResumeOResto()
    {
        var agendamentos = Enumerable.Range(0, 5)
            .Select(i => Dto($"Item {i}", new DateTime(2026, 11, 5, 8 + i, 0, 0), new DateTime(2026, 11, 5, 8 + i, 30, 0)))
            .ToList();

        var dia = GradeDoMes.Montar(2026, 11, new DateTime(2026, 11, 2), agendamentos)
            .Single(d => d.Data == new DateTime(2026, 11, 5));

        Assert.Equal(5, dia.Total);
        Assert.Equal(GradeDoMes.MaximoPorDia, dia.Chips.Count);
        Assert.True(dia.TemExcedente);
        Assert.Equal(2, dia.Excedente);
        Assert.Equal("+2 mais", dia.ExcedenteTexto);
        Assert.Equal("08:00 Item 0", dia.Chips[0].Texto);
    }

    [Fact]
    public void Montar_ExatamenteOMaximo_NaoTemExcedente()
    {
        var agendamentos = Enumerable.Range(0, GradeDoMes.MaximoPorDia)
            .Select(i => Dto($"Item {i}", new DateTime(2026, 11, 5, 8 + i, 0, 0), new DateTime(2026, 11, 5, 8 + i, 30, 0)));

        var dia = GradeDoMes.Montar(2026, 11, new DateTime(2026, 11, 2), agendamentos)
            .Single(d => d.Data == new DateTime(2026, 11, 5));

        Assert.False(dia.TemExcedente);
    }

    [Fact]
    public void Montar_AgendamentoDosMesesVizinhosApareceNasCelulasCinzas()
    {
        var dias = GradeDoMes.Montar(2026, 11, new DateTime(2026, 11, 2),
        [
            Dto("Fim do ano", new DateTime(2026, 12, 5, 9, 0, 0), new DateTime(2026, 12, 5, 10, 0, 0))
        ]);

        var dia = dias.Single(d => d.Data == new DateTime(2026, 12, 5));
        Assert.False(dia.DoMesAtual);
        Assert.Equal(1, dia.Total);
    }
}
