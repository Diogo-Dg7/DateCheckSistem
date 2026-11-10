using System.Net;
using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace DateCheck.Integracoes.Tests.Apoio;

/// <summary>Uma requisição como o servidor falso a recebeu (o corpo já lido, porque a mensagem é descartada).</summary>
public sealed record RequisicaoRecebida(
    HttpMethod Metodo, Uri Uri, string? Autorizacao, string? NotionVersion, string? Corpo);

/// <summary>
/// Servidor HTTP falso: devolve as respostas na ordem em que foram enfileiradas e registra tudo o que recebeu.
/// Sem rede de verdade nos testes.
/// </summary>
public sealed class HandlerFalso : HttpMessageHandler
{
    private readonly Queue<Func<RequisicaoRecebida, HttpResponseMessage>> _respostas = new();

    public List<RequisicaoRecebida> Recebidas { get; } = [];

    /// <summary>Resposta usada quando a fila acaba (padrão: falha o teste com 599).</summary>
    public Func<RequisicaoRecebida, HttpResponseMessage>? Padrao { get; set; }

    public HandlerFalso Responder(HttpStatusCode status, string? json = null, TimeSpan? retryAfter = null) =>
        Responder(_ => Montar(status, json, retryAfter));

    public HandlerFalso Responder(Func<RequisicaoRecebida, HttpResponseMessage> resposta)
    {
        _respostas.Enqueue(resposta);
        return this;
    }

    public HandlerFalso Falhar(Exception excecao) => Responder(_ => throw excecao);

    public static HttpResponseMessage Montar(HttpStatusCode status, string? json = null, TimeSpan? retryAfter = null)
    {
        var resposta = new HttpResponseMessage(status);
        if (json is not null)
            resposta.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (retryAfter is { } espera)
            resposta.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(espera);
        return resposta;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recebida = new RequisicaoRecebida(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("Notion-Version", out var v) ? v.Single() : null,
            corpo);
        Recebidas.Add(recebida);

        if (_respostas.TryDequeue(out var resposta))
            return resposta(recebida);

        return Padrao?.Invoke(recebida)
            ?? Montar((HttpStatusCode)599, "{\"fim_da_fila\":true}");
    }
}

/// <summary>
/// <see cref="FakeTimeProvider"/> que SINALIZA cada timer criado (cada <c>Task.Delay</c> que usa este relógio cria
/// um). Os testes esperam o timer existir antes de avançar o relógio: assim não dependem de "dar um tempo" real
/// para o código sob teste chegar até a espera, e continuam corretos com a máquina lenta ou sob carga.
/// </summary>
public sealed class FakeTempo : FakeTimeProvider
{
    private readonly SemaphoreSlim _sinal = new(0);
    private int _criados;
    private int _consumidos;

    /// <summary>Quantos timers (esperas) já foram criados neste relógio.</summary>
    public int TimersCriados => Volatile.Read(ref _criados);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _criados);
        _sinal.Release();
        return timer;
    }

    /// <summary>
    /// Espera, sem depender de tempo real, até que <paramref name="total"/> timers tenham sido criados (contagem
    /// acumulada). O limite só existe para o teste falhar, em vez de travar, se houver um bug.
    /// </summary>
    public async Task AguardarTimersAsync(int total, CancellationToken cancellationToken = default)
    {
        while (_consumidos < total)
        {
            if (!await _sinal.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken))
                throw new TimeoutException($"Esperava {total} espera(s) no relógio, mas só houve {TimersCriados}.");

            _consumidos++;
        }
    }

    /// <summary>Espera mais um timer além dos já consumidos.</summary>
    public Task AguardarProximoTimerAsync(CancellationToken cancellationToken = default) =>
        AguardarTimersAsync(Volatile.Read(ref _consumidos) + 1, cancellationToken);
}

public static class RelogioDeTeste
{
    /// <summary>
    /// Deixa a tarefa terminar avançando o relógio SÓ quando ela está esperando um timer: espera o próximo timer
    /// existir (ou a tarefa terminar) e então avança <paramref name="passo"/>. Sem polling e sem prazo em tempo real;
    /// o passo é grande de propósito (as asserções de tempo dos testes são "pelo menos X").
    /// </summary>
    public static async Task<T> AvancarAteConcluirAsync<T>(
        this Task<T> tarefa, FakeTempo relogio, TimeSpan? passo = null)
    {
        var avanco = passo ?? TimeSpan.FromMinutes(5);

        while (!tarefa.IsCompleted)
        {
            using var desistir = new CancellationTokenSource();
            var proximoTimer = relogio.AguardarProximoTimerAsync(desistir.Token);

            var primeira = await Task.WhenAny(tarefa, proximoTimer);
            if (primeira == tarefa)
            {
                await desistir.CancelAsync(); // não deixa uma espera pendurada consumindo um sinal futuro
                break;
            }

            await proximoTimer; // propaga um eventual timeout
            relogio.Advance(avanco);
        }

        return await tarefa;
    }

    /// <summary>
    /// Só para asserções NEGATIVAS ("ainda não aconteceu"): dá um tempo real curto para um disparo indevido aparecer.
    /// Se a máquina estiver lenta, o pior caso é um falso "passou", nunca uma falha falsa.
    /// </summary>
    public static Task ConfirmarQueNadaAconteceu() => Task.Delay(50);
}
