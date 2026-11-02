using DateCheck.Application.Dtos;
using DateCheck.Application.Servicos;
using DateCheck.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace DateCheck.Infrastructure.Tests;

/// <summary>Os serviços da Application funcionando de ponta a ponta sobre o repositório real.</summary>
public class ServicosComBancoRealTests : TesteDeIntegracaoBase
{
    public ServicosComBancoRealTests(BancoDeDadosFixture banco) : base(banco)
    {
    }

    [Fact]
    public async Task Repetir_GravaSerieEConflitoEnxergaOQueEstaNoBanco()
    {
        var agendamentos = Banco.Services.GetRequiredService<IAgendamentoService>();
        var repeticao = Banco.Services.GetRequiredService<IRepeticaoService>();
        var conflitos = Banco.Services.GetRequiredService<IConflitoService>();

        var original = await agendamentos.CriarAsync(new NovoAgendamentoRequest(
            "Academia", new DateTime(2026, 1, 31, 7, 0, 0), new DateTime(2026, 1, 31, 8, 0, 0)));
        await agendamentos.CriarAsync(new NovoAgendamentoRequest(
            "Médico", new DateTime(2026, 3, 31, 7, 30, 0), new DateTime(2026, 3, 31, 8, 30, 0)));

        var resultado = await repeticao.RepetirAsync(new RepetirRequest(
            original.Id, FrequenciaRepeticao.Mensal, new DateTime(2026, 4, 30)));

        Assert.Equal(
            [new DateTime(2026, 2, 28, 7, 0, 0), new DateTime(2026, 3, 31, 7, 0, 0), new DateTime(2026, 4, 30, 7, 0, 0)],
            resultado.Copias.Select(c => c.Agendamento.Inicio));
        Assert.Equal(["Médico"], resultado.Copias[1].Conflitos.Select(c => c.Titulo));

        var doMes = await agendamentos.ListarDoMesAsync(2026, 3);
        Assert.Equal(["Academia", "Médico"], doMes.Select(a => a.Titulo));
        Assert.Equal(resultado.SerieId, doMes[0].SerieId);

        var emConflito = await conflitos.BuscarConflitosAsync(
            new DateTime(2026, 3, 31, 7, 45, 0), new DateTime(2026, 3, 31, 8, 15, 0));
        Assert.Equal(2, emConflito.Count);
    }

    [Fact]
    public async Task Duplicar_GravaCopiaPendenteDeUmCancelado()
    {
        var agendamentos = Banco.Services.GetRequiredService<IAgendamentoService>();
        var duplicacao = Banco.Services.GetRequiredService<IDuplicacaoService>();
        var original = await agendamentos.CriarAsync(new NovoAgendamentoRequest(
            "Reunião", new DateTime(2026, 11, 10, 9, 0, 0), new DateTime(2026, 11, 10, 10, 0, 0)));
        await agendamentos.CancelarAsync(original.Id);

        var copia = await duplicacao.DuplicarAsync(original.Id, new DateTime(2026, 11, 17, 9, 0, 0));

        var lida = await agendamentos.ObterAsync(copia.Id);
        Assert.Equal(StatusAgendamento.Pendente, lida.Status);
        Assert.Equal(StatusAgendamento.Cancelado, (await agendamentos.ObterAsync(original.Id)).Status);
    }
}
