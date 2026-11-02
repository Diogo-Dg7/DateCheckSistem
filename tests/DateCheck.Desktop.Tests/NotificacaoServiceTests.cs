using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Notificacoes;
using DateCheck.Desktop.Tests.Apoio;
using Microsoft.Extensions.Logging.Abstractions;

namespace DateCheck.Desktop.Tests;

public class NotificacaoServiceTests
{
    // "Agora" do ambiente: 02/11/2026 13:30:00
    private readonly Ambiente _ambiente = new();
    private readonly FakeNotificador _notificador = new();
    private readonly FakePreferenciasService _preferencias = new();
    private readonly NotificacaoService _servico;

    public NotificacaoServiceTests()
    {
        _servico = new NotificacaoService(
            _ambiente.Agendamentos, _notificador, _preferencias, _ambiente.Relogio,
            NullLogger<NotificacaoService>.Instance);
    }

    private static DateTime Em(int minutos, int segundos = 0) => Ambiente.Agora.AddMinutes(minutos).AddSeconds(segundos);

    private Domain.Entidades.Agendamento Semear(string titulo, DateTime inicio, int duracaoMinutos = 30) =>
        _ambiente.Semear(titulo, inicio, inicio.AddMinutes(duracaoMinutos));

    // ---------- quem é notificado ----------

    [Fact]
    public async Task PendenteQueComecaDentroDaAntecedencia_EhNotificadoComOsMinutosRestantes()
    {
        Semear("Reunião", Em(7));

        await _servico.VerificarAsync();

        var (agendamento, minutos) = Assert.Single(_notificador.Notificacoes);
        Assert.Equal("Reunião", agendamento.Titulo);
        Assert.Equal(7, minutos);
    }

    [Fact]
    public async Task ComecaAlemDaAntecedencia_NaoEhNotificadoAindaMasEhQuandoChegaAHora()
    {
        Semear("Reunião", Em(11));

        await _servico.VerificarAsync();
        Assert.Empty(_notificador.Notificacoes);

        _ambiente.DefinirAgora(Em(1)); // agora faltam 10 minutos
        await _servico.VerificarAsync();
        Assert.Equal(["Reunião"], _notificador.Titulos);
    }

    [Fact]
    public async Task ExatamenteNoLimiteDaAntecedencia_EhNotificado()
    {
        Semear("No limite", Em(10));

        await _servico.VerificarAsync();

        Assert.Equal(10, Assert.Single(_notificador.Notificacoes).MinutosRestantes);
    }

    [Fact]
    public async Task UmSegundoAlemDoLimite_NaoEhNotificado()
    {
        Semear("Quase", Em(10, 1));

        await _servico.VerificarAsync();

        Assert.Empty(_notificador.Notificacoes);
    }

    [Fact]
    public async Task ComecandoExatamenteAgora_EhNotificadoComZeroMinutos()
    {
        Semear("Agora", Em(0));

        await _servico.VerificarAsync();

        Assert.Equal(0, Assert.Single(_notificador.Notificacoes).MinutosRestantes);
    }

    [Theory]
    [InlineData(0, 30, 1)]   // 30 s restantes -> 1 min
    [InlineData(9, 30, 10)]  // 9 min 30 s -> 10 min
    [InlineData(5, 0, 5)]
    public async Task MinutosRestantes_ArredondaParaCima(int minutos, int segundos, int esperado)
    {
        Semear("Item", Em(minutos, segundos));

        await _servico.VerificarAsync();

        Assert.Equal(esperado, Assert.Single(_notificador.Notificacoes).MinutosRestantes);
    }

    [Fact]
    public async Task CompromissoQueJaComecou_NaoEhNotificado()
    {
        Semear("Em andamento", Em(-1), duracaoMinutos: 60);
        Semear("Começou há 1 segundo", Em(0, -1), duracaoMinutos: 60);

        await _servico.VerificarAsync();

        Assert.Empty(_notificador.Notificacoes);
    }

    [Fact]
    public async Task VoltaDaSuspensao_NaoNotificaOQueJaPassouMasNotificaOQueEstaPorVir()
    {
        Semear("Perdido durante a suspensão", Em(30));
        Semear("Ainda por vir", Em(3 * 60 + 5));
        await _servico.VerificarAsync(); // antes de suspender: nada na janela
        Assert.Empty(_notificador.Notificacoes);

        _ambiente.DefinirAgora(Em(3 * 60)); // o notebook volta 3 horas depois
        await _servico.VerificarAsync();

        Assert.Equal(["Ainda por vir"], _notificador.Titulos);
    }

    [Fact]
    public async Task Cancelado_NaoEhNotificado()
    {
        Semear("Cancelado", Em(5)).Cancelar();

        await _servico.VerificarAsync();

        Assert.Empty(_notificador.Notificacoes);
    }

    [Fact]
    public async Task Concluido_NaoEhNotificado()
    {
        Semear("Concluído", Em(5)).Concluir();

        await _servico.VerificarAsync();

        Assert.Empty(_notificador.Notificacoes);
    }

    [Fact]
    public async Task Reaberto_PassaAserNotificado()
    {
        var agendamento = Semear("Reaberto", Em(5));
        agendamento.Cancelar();
        await _servico.VerificarAsync();
        Assert.Empty(_notificador.Notificacoes);

        agendamento.Reabrir();
        await _servico.VerificarAsync();

        Assert.Equal(["Reaberto"], _notificador.Titulos);
    }

    // ---------- sem repetir ----------

    [Fact]
    public async Task PassadasSeguidas_NaoNotificamDuasVezesOMesmoCompromisso()
    {
        Semear("Uma vez só", Em(8));

        for (var i = 0; i < 5; i++)
        {
            await _servico.VerificarAsync();
            _ambiente.DefinirAgora(Ambiente.Agora.AddSeconds(30 * (i + 1)));
        }

        Assert.Single(_notificador.Notificacoes);
    }

    [Fact]
    public async Task CompromissoNotificadoEDepoisCancelado_NaoVoltaAserNotificadoAoReabrir()
    {
        var agendamento = Semear("Avisado", Em(5));
        await _servico.VerificarAsync();

        agendamento.Cancelar();
        await _servico.VerificarAsync();
        agendamento.Reabrir();
        await _servico.VerificarAsync();

        Assert.Single(_notificador.Notificacoes);
    }

    [Fact]
    public async Task Duplicados_CadaCopiaTemOSeuAvisoMasNaoSeRepetem()
    {
        var original = Semear("Aula de inglês", Em(6));
        _ambiente.Repositorio.Semear(original.CopiarPara(Em(6))); // outro compromisso, mesmo título e horário

        await _servico.VerificarAsync();
        await _servico.VerificarAsync();

        Assert.Equal(2, _notificador.Notificacoes.Count);
        Assert.Equal(2, _notificador.Notificacoes.Select(n => n.Agendamento.Id).Distinct().Count());
    }

    [Fact]
    public async Task Reagendado_ParaOutroHorarioNaJanela_EhNotificadoDeNovo()
    {
        var agendamento = Semear("Reunião", Em(8));
        await _servico.VerificarAsync();
        Assert.Single(_notificador.Notificacoes);

        agendamento.Reagendar(Em(9), Em(40));
        await _servico.VerificarAsync();

        Assert.Equal(2, _notificador.Notificacoes.Count);
        Assert.Equal(9, _notificador.Notificacoes[1].MinutosRestantes);
    }

    [Fact]
    public async Task Reagendado_ParaForaDaJanela_NaoEhNotificado()
    {
        var agendamento = Semear("Reunião", Em(30));
        agendamento.Reagendar(Em(120), Em(150));

        await _servico.VerificarAsync();

        Assert.Empty(_notificador.Notificacoes);
    }

    [Fact]
    public async Task Reagendado_ParaOPassado_NaoEhNotificado()
    {
        var agendamento = Semear("Reunião", Em(8));
        await _servico.VerificarAsync();

        agendamento.Reagendar(Em(-20), Em(-10));
        await _servico.VerificarAsync();

        Assert.Single(_notificador.Notificacoes);
    }

    // ---------- preferências ----------

    [Fact]
    public async Task MudarAAntecedencia_ValeNaProximaPassada()
    {
        Semear("Em 25 min", Em(25));
        await _servico.VerificarAsync();
        Assert.Empty(_notificador.Notificacoes);

        _preferencias.Salvar(new Preferencias { MinutosAntes = 30 });
        await _servico.VerificarAsync();

        Assert.Equal(["Em 25 min"], _notificador.Titulos);
    }

    [Fact]
    public async Task AntecedenciaZero_SoNotificaQuemComecaAgora()
    {
        _preferencias.Salvar(new Preferencias { MinutosAntes = 0 });
        Semear("Agora", Em(0));
        Semear("Daqui a pouco", Em(1));

        await _servico.VerificarAsync();

        Assert.Equal(["Agora"], _notificador.Titulos);
    }

    // ---------- falhas ----------

    [Fact]
    public async Task SeOToastFalha_TentaDeNovoNaProximaPassadaESoDepoisMarcaComoAvisado()
    {
        Semear("Reunião", Em(5));
        _notificador.FalhasPendentes = 1;

        await _servico.VerificarAsync();
        Assert.Empty(_notificador.Notificacoes);

        await _servico.VerificarAsync();
        Assert.Single(_notificador.Notificacoes);

        await _servico.VerificarAsync();
        Assert.Single(_notificador.Notificacoes);
        Assert.Equal(2, _notificador.Tentativas);
    }

    [Fact]
    public async Task UmaFalhaNaoImpedeOsDemaisAvisosDaMesmaPassada()
    {
        Semear("Primeiro", Em(3));
        Semear("Segundo", Em(4));
        _notificador.FalhasPendentes = 1;

        await _servico.VerificarAsync();

        Assert.Equal(["Segundo"], _notificador.Titulos);
    }

    // ---------- o laço em segundo plano ----------

    [Fact]
    public async Task EmSegundoPlano_NotificaNaAberturaEACada30Segundos()
    {
        Semear("Na abertura", Em(2));
        await _servico.StartAsync(CancellationToken.None);
        try
        {
            await EsperarAsync(() => _notificador.Notificacoes.Count == 1);

            Semear("Criado depois", Em(6));
            _ambiente.Relogio.Advance(NotificacaoService.IntervaloDeVerificacao);

            await EsperarAsync(() => _notificador.Notificacoes.Count == 2);
            Assert.Equal(["Na abertura", "Criado depois"], _notificador.Titulos);
        }
        finally
        {
            await _servico.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EmSegundoPlano_BancoForaDoArNaoDerrubaOLacoERecuperaDepois()
    {
        Semear("Reunião", Em(8));
        _ambiente.Repositorio.FalharConsultas = true;
        await _servico.StartAsync(CancellationToken.None);
        try
        {
            await EsperarAsync(() => _ambiente.Repositorio.Consultas >= 1);
            Assert.Empty(_notificador.Notificacoes);

            _ambiente.Repositorio.FalharConsultas = false;
            _ambiente.Relogio.Advance(NotificacaoService.IntervaloDeVerificacao);

            await EsperarAsync(() => _notificador.Notificacoes.Count == 1);
        }
        finally
        {
            await _servico.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EmSegundoPlano_PararEncerraSemErro()
    {
        await _servico.StartAsync(CancellationToken.None);

        var parou = _servico.StopAsync(CancellationToken.None);

        await parou.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task EsperarAsync(Func<bool> condicao)
    {
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!condicao())
        {
            if (DateTime.UtcNow > limite)
                throw new TimeoutException("A condição esperada não ocorreu a tempo.");

            await Task.Delay(10);
        }
    }
}
