using System.Net;
using System.Text;
using DateCheck.Integracoes.Rede;
using DateCheck.Integracoes.Tests.Apoio;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Integracoes.Tests;

public class RedeHandlersTests
{
    private readonly FakeTempo _relogio = new();
    private readonly HandlerFalso _servidor = new();

    private HttpClient CriarCliente(ReintentoHandler? reintento = null, LimitadorDeTaxaHandler? limitador = null)
    {
        DelegatingHandler cadeia = reintento ?? new ReintentoHandler(_relogio, fatorDeJitter: 0);
        if (limitador is not null)
        {
            limitador.InnerHandler = _servidor;
            cadeia.InnerHandler = limitador;
        }
        else
        {
            cadeia.InnerHandler = _servidor;
        }

        return new HttpClient(cadeia);
    }

    private static Task<HttpResponseMessage> Get(HttpClient cliente, CancellationToken ct = default) =>
        cliente.GetAsync("https://api.exemplo.test/x", ct);

    // ---------- reintento ----------

    [Fact]
    public async Task Sucesso_NaoReintentaNemEspera()
    {
        _servidor.Responder(HttpStatusCode.OK, "{}");
        using var cliente = CriarCliente();

        var resposta = await Get(cliente);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Single(_servidor.Recebidas);
    }

    [Fact]
    public async Task Http429_RespeitaORetryAfterEDepoisTenta()
    {
        _servidor.Responder(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(7)).Responder(HttpStatusCode.OK, "{}");
        using var cliente = CriarCliente();

        var tarefa = Get(cliente);
        await _relogio.AguardarTimersAsync(1); // o reintento já está esperando o Retry-After
        Assert.Single(_servidor.Recebidas);

        _relogio.Advance(TimeSpan.FromSeconds(6.9));
        Assert.False(tarefa.IsCompleted); // ainda dentro do Retry-After (o timer não venceu: nada a esperar em tempo real)
        Assert.Single(_servidor.Recebidas);

        _relogio.Advance(TimeSpan.FromSeconds(0.2));
        var resposta = await tarefa;

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(2, _servidor.Recebidas.Count);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(529)]
    [InlineData(408)]
    public async Task ErrosTransitorios_SaoRefeitos(int status)
    {
        _servidor.Responder((HttpStatusCode)status).Responder(HttpStatusCode.OK, "{}");
        using var cliente = CriarCliente();

        var resposta = await Get(cliente).AvancarAteConcluirAsync(_relogio);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(2, _servidor.Recebidas.Count);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    public async Task ErrosDeCliente_NuncaSaoRefeitos(int status)
    {
        _servidor.Responder((HttpStatusCode)status, "{}");
        using var cliente = CriarCliente();

        var resposta = await Get(cliente);

        Assert.Equal((HttpStatusCode)status, resposta.StatusCode);
        Assert.Single(_servidor.Recebidas);
    }

    [Fact]
    public async Task SemRetryAfter_UsaBackoffExponencial_1_2_4_Segundos()
    {
        _servidor.Responder(HttpStatusCode.ServiceUnavailable)
            .Responder(HttpStatusCode.ServiceUnavailable)
            .Responder(HttpStatusCode.ServiceUnavailable)
            .Responder(HttpStatusCode.OK, "{}");
        using var cliente = CriarCliente();
        var inicio = _relogio.GetUtcNow();

        var tarefa = Get(cliente);
        var requisicoes = 1;
        foreach (var segundos in new[] { 1, 2, 4 })
        {
            await _relogio.AguardarTimersAsync(requisicoes); // o reintento está esperando o backoff
            Assert.Equal(requisicoes, _servidor.Recebidas.Count);

            _relogio.Advance(TimeSpan.FromSeconds(segundos) - TimeSpan.FromMilliseconds(1));
            Assert.Equal(requisicoes, _servidor.Recebidas.Count); // 1 ms antes: ainda esperando
            Assert.False(tarefa.IsCompleted);

            _relogio.Advance(TimeSpan.FromMilliseconds(1));
            requisicoes++;
        }

        var resposta = await tarefa;

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(4, _servidor.Recebidas.Count);
        Assert.Equal(TimeSpan.FromSeconds(7), _relogio.GetUtcNow() - inicio);
    }

    [Fact]
    public async Task Http429Esgotado_DevolveAUltimaRespostaDepoisDeTodasAsTentativas()
    {
        _servidor.Padrao = _ => HandlerFalso.Montar(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(1));
        using var cliente = CriarCliente(new ReintentoHandler(_relogio, maximoDeReintentos: 3, fatorDeJitter: 0));

        var resposta = await Get(cliente).AvancarAteConcluirAsync(_relogio);

        Assert.Equal(HttpStatusCode.TooManyRequests, resposta.StatusCode);
        Assert.Equal(4, _servidor.Recebidas.Count); // 1 + 3 reintentos
    }

    [Fact]
    public async Task RetryAfterGigante_NaoEhEsperado_EDevolveOCaso429NaHora()
    {
        _servidor.Responder(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromHours(1));
        using var cliente = CriarCliente();

        var resposta = await Get(cliente); // sem avançar o relógio: não pode ter esperado

        Assert.Equal(HttpStatusCode.TooManyRequests, resposta.StatusCode);
        Assert.Single(_servidor.Recebidas);
    }

    [Fact]
    public async Task FalhaDeRede_EhRefeita()
    {
        _servidor.Falhar(new HttpRequestException("rede caiu")).Responder(HttpStatusCode.OK, "{}");
        using var cliente = CriarCliente();

        var resposta = await Get(cliente).AvancarAteConcluirAsync(_relogio);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(2, _servidor.Recebidas.Count);
    }

    [Fact]
    public async Task FalhaDeRedePersistente_EhRelancadaDepoisDeEsgotarOsReintentos()
    {
        var servidor = new HandlerFalso { Padrao = _ => throw new HttpRequestException("sempre falha") };
        var reintento = new ReintentoHandler(_relogio, maximoDeReintentos: 2, fatorDeJitter: 0) { InnerHandler = servidor };
        using var cliente = new HttpClient(reintento);

        await Assert.ThrowsAsync<HttpRequestException>(() => Get(cliente).AvancarAteConcluirAsync(_relogio));

        Assert.Equal(3, servidor.Recebidas.Count); // 1 + 2 reintentos
    }

    [Fact]
    public async Task Reintento_RepeteMetodoCorpoEAutorizacaoSemPerderNada()
    {
        _servidor.Responder(HttpStatusCode.ServiceUnavailable).Responder(HttpStatusCode.OK, "{}");
        using var cliente = CriarCliente();
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "https://api.exemplo.test/consulta")
        {
            Content = new StringContent("{\"filtro\":\"àéç\"}", Encoding.UTF8, "application/json")
        };
        pedido.Headers.Authorization = new("Bearer", "segredo-de-teste");
        pedido.Headers.Add("Notion-Version", "2026-03-11");

        var resposta = await cliente.SendAsync(pedido).AvancarAteConcluirAsync(_relogio);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(2, _servidor.Recebidas.Count);
        Assert.All(_servidor.Recebidas, r =>
        {
            Assert.Equal(HttpMethod.Post, r.Metodo);
            Assert.Equal("{\"filtro\":\"àéç\"}", r.Corpo);
            Assert.Equal("Bearer segredo-de-teste", r.Autorizacao);
            Assert.Equal("2026-03-11", r.NotionVersion);
        });
    }

    [Fact]
    public async Task Cancelamento_DuranteAEspera_InterrompeSemNovaTentativa()
    {
        _servidor.Responder(HttpStatusCode.TooManyRequests, "{}", TimeSpan.FromSeconds(30)).Responder(HttpStatusCode.OK, "{}");
        using var cliente = CriarCliente();
        using var cancelamento = new CancellationTokenSource();

        var tarefa = Get(cliente, cancelamento.Token);
        await _relogio.AguardarTimersAsync(1); // já está esperando o Retry-After
        await cancelamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarefa);
        Assert.Single(_servidor.Recebidas);
    }

    // ---------- limitador de taxa ----------

    [Fact]
    public async Task Limitador_RequisicoesSimultaneasSaemEspacadas()
    {
        _servidor.Padrao = _ => HandlerFalso.Montar(HttpStatusCode.OK, "{}");
        var limitador = new LimitadorDeTaxaHandler(_relogio, TimeSpan.FromMilliseconds(350));
        using var cliente = CriarCliente(limitador: limitador);

        var t1 = Get(cliente);
        var t2 = Get(cliente);
        var t3 = Get(cliente);
        await _relogio.AguardarTimersAsync(2); // t2 e t3 estão esperando a vez; t1 sai na hora
        await t1;
        Assert.Single(_servidor.Recebidas);

        _relogio.Advance(TimeSpan.FromMilliseconds(349));
        Assert.Single(_servidor.Recebidas); // 1 ms antes da vez de t2
        Assert.False(t2.IsCompleted);

        _relogio.Advance(TimeSpan.FromMilliseconds(1));
        await t2;
        Assert.Equal(2, _servidor.Recebidas.Count);
        Assert.False(t3.IsCompleted);

        _relogio.Advance(TimeSpan.FromMilliseconds(350));
        await t3;
        Assert.Equal(3, _servidor.Recebidas.Count);
    }

    [Fact]
    public async Task Limitador_DepoisDeUmTempoParado_NaoAtrasaAProximaRequisicao()
    {
        _servidor.Padrao = _ => HandlerFalso.Montar(HttpStatusCode.OK, "{}");
        var limitador = new LimitadorDeTaxaHandler(_relogio, TimeSpan.FromMilliseconds(350));
        using var cliente = CriarCliente(limitador: limitador);

        await Get(cliente);
        _relogio.Advance(TimeSpan.FromSeconds(10));
        await Get(cliente); // se esperasse, travaria: o relógio falso não anda sozinho

        Assert.Equal(2, _servidor.Recebidas.Count);
    }

    [Fact]
    public async Task Limitador_CadaTentativaDoReintentoPassaPeloLimitador()
    {
        // O limitador fica DENTRO do reintento: um reintento também respeita o ritmo.
        _servidor.Responder(HttpStatusCode.ServiceUnavailable).Responder(HttpStatusCode.OK, "{}");
        var limitador = new LimitadorDeTaxaHandler(_relogio, TimeSpan.FromSeconds(5));
        using var cliente = CriarCliente(limitador: limitador);
        var inicio = _relogio.GetUtcNow();

        await Get(cliente).AvancarAteConcluirAsync(_relogio);

        Assert.True(_relogio.GetUtcNow() - inicio >= TimeSpan.FromSeconds(5));
        Assert.Equal(2, _servidor.Recebidas.Count);
    }
}
