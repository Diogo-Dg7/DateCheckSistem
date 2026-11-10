using DateCheck.Application.Servicos;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Desktop.Integracoes;
using DateCheck.Desktop.Tests.Apoio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Desktop.Tests;

/// <summary>Sincronização em segundo plano, recarga das telas e balão de reconexão.</summary>
public sealed class SincronizacaoNotionTests
{
    private readonly FakeTimeProvider _relogio = new();
    private readonly SincronizacaoFalsa _sincronizacao = new();
    private readonly FakePreferenciasService _preferencias = new();

    private SincronizacaoNotionBackgroundService CriarServico() =>
        new(_sincronizacao, _preferencias, _relogio, NullLogger<SincronizacaoNotionBackgroundService>.Instance);

    /// <summary>
    /// Só para asserções NEGATIVAS ("ainda não sincronizou"): dá um tempo para um disparo indevido aparecer. Se a
    /// máquina estiver lenta, o pior caso é um falso "passou", nunca uma falha falsa. Tudo que DEVE acontecer é
    /// esperado pelo sinal do serviço (<see cref="SincronizacaoFalsa.AguardarChamadasAsync"/>), sem depender de tempo real.
    /// </summary>
    private static Task ConfirmarQueNadaAconteceu() => Task.Delay(50);

    // ---------- segundo plano ----------

    [Fact]
    public async Task SincronizaNaAbertura_SemEsperarOPrimeiroIntervalo()
    {
        using var servico = CriarServico();

        await servico.StartAsync(CancellationToken.None);
        await _sincronizacao.AguardarChamadasAsync(1);

        Assert.Equal(1, _sincronizacao.Chamadas);
        await servico.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DepoisSincronizaACadaDezMinutosPorPadrao_ComSincronizacaoAutomatica()
    {
        using var servico = CriarServico();
        await servico.StartAsync(CancellationToken.None);
        await _sincronizacao.AguardarChamadasAsync(1);

        _relogio.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        await ConfirmarQueNadaAconteceu();
        Assert.Equal(1, _sincronizacao.Chamadas);

        _relogio.Advance(TimeSpan.FromSeconds(1));
        await _sincronizacao.AguardarChamadasAsync(2);

        _relogio.Advance(TimeSpan.FromMinutes(10));
        await _sincronizacao.AguardarChamadasAsync(3);

        Assert.All(_sincronizacao.Manuais, manual => Assert.False(manual)); // "Sincronizar agora" é que é manual
        await servico.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UsaOIntervaloDasPreferencias()
    {
        _preferencias.Salvar(new Preferencias { Notion = new NotionPreferencias { IntervaloEmMinutos = 5 } });
        using var servico = CriarServico();
        await servico.StartAsync(CancellationToken.None);
        await _sincronizacao.AguardarChamadasAsync(1);

        _relogio.Advance(TimeSpan.FromMinutes(5));
        await _sincronizacao.AguardarChamadasAsync(2);

        await servico.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MudarOIntervaloNasConfiguracoes_ValeNaHora()
    {
        _preferencias.Salvar(new Preferencias { Notion = new NotionPreferencias { IntervaloEmMinutos = 60 } });
        using var servico = CriarServico();
        await servico.StartAsync(CancellationToken.None);
        await _sincronizacao.AguardarChamadasAsync(1);

        _preferencias.Salvar(new Preferencias { Notion = new NotionPreferencias { IntervaloEmMinutos = 5 } });
        _relogio.Advance(TimeSpan.FromMinutes(5));
        await _sincronizacao.AguardarChamadasAsync(2);

        await servico.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExcecaoInesperada_NaoMataOLaco()
    {
        _sincronizacao.ExcecaoNaProxima = new InvalidOperationException("boom");
        using var servico = CriarServico();
        await servico.StartAsync(CancellationToken.None);
        await _sincronizacao.AguardarChamadasAsync(1); // a que lançou

        _relogio.Advance(TimeSpan.FromMinutes(10));
        await _sincronizacao.AguardarChamadasAsync(2); // o laço seguiu vivo

        await servico.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AoParar_NaoSincronizaMais()
    {
        using var servico = CriarServico();
        await servico.StartAsync(CancellationToken.None);
        await _sincronizacao.AguardarChamadasAsync(1);

        await servico.StopAsync(CancellationToken.None);
        _relogio.Advance(TimeSpan.FromMinutes(30));
        await ConfirmarQueNadaAconteceu();

        Assert.Equal(1, _sincronizacao.Chamadas);
    }

    // ---------- reações ----------

    [Fact]
    public void DadosAlterados_RecarregamATelaAberta()
    {
        SynchronizationContext.SetSynchronizationContext(null); // sem contexto: roda direto, determinístico
        var navegacao = new FakeNavegacao();
        using var reacoes = new ReacoesDaSincronizacao(_sincronizacao, navegacao, new AvisoDeBandejaFalso(), NullLogger<ReacoesDaSincronizacao>.Instance);

        _sincronizacao.DispararDadosAlterados();
        _sincronizacao.DispararDadosAlterados();

        Assert.Equal(2, navegacao.Recarregamentos);
    }

    [Fact]
    public void ReconexaoNecessaria_MostraOBalaoComAMensagemSemSegredos()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var bandeja = new AvisoDeBandejaFalso();
        using var reacoes = new ReacoesDaSincronizacao(_sincronizacao, new FakeNavegacao(), bandeja, NullLogger<ReacoesDaSincronizacao>.Instance);

        _sincronizacao.DispararReconexao();

        var (titulo, mensagem) = Assert.Single(bandeja.Baloes);
        Assert.Equal("DateCheck", titulo);
        Assert.Equal(ReacoesDaSincronizacao.MensagemDeReconexao, mensagem);
        Assert.Contains("Configurações", mensagem);
    }

    [Fact]
    public void ErroAoRecarregar_NaoPropagaParaQuemDisparouOEvento()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var navegacao = new NavegacaoQueFalha();
        using var reacoes = new ReacoesDaSincronizacao(_sincronizacao, navegacao, new AvisoDeBandejaFalso(), NullLogger<ReacoesDaSincronizacao>.Instance);

        _sincronizacao.DispararDadosAlterados(); // se propagasse, derrubaria a sincronização

        Assert.Equal(1, navegacao.Tentativas);
    }

    [Fact]
    public void AposDescartar_NaoReageMais()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var navegacao = new FakeNavegacao();
        var reacoes = new ReacoesDaSincronizacao(_sincronizacao, navegacao, new AvisoDeBandejaFalso(), NullLogger<ReacoesDaSincronizacao>.Instance);

        reacoes.Dispose();
        _sincronizacao.DispararDadosAlterados();

        Assert.Equal(0, navegacao.Recarregamentos);
    }

    // ---------- fakes ----------

    private sealed class AvisoDeBandejaFalso : IAvisoDeBandeja
    {
        public List<(string Titulo, string Mensagem)> Baloes { get; } = [];

        public void MostrarBalao(string titulo, string mensagem) => Baloes.Add((titulo, mensagem));
    }

    private sealed class NavegacaoQueFalha : DateCheck.Desktop.Navegacao.INavegacao
    {
        public int Tentativas { get; private set; }

        public Task MostrarHojeAsync(DateTime? dia = null) => Task.CompletedTask;

        public Task MostrarCalendarioAsync() => Task.CompletedTask;

        public Task RecarregarAtualAsync()
        {
            Tentativas++;
            throw new InvalidOperationException("banco fora do ar");
        }
    }
}
