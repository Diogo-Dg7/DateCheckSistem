using System.Net;

namespace DateCheck.Integracoes.Rede;

/// <summary>
/// Refaz a requisição quando a API pede (429) ou está instável (5xx, 529) e em falhas de rede, com backoff
/// exponencial e jitter. Respeita <c>Retry-After</c> (segundos); um valor acima de <see cref="EsperaMaximaPorTentativa"/>
/// não é esperado: devolve a resposta 429 para o chamador decidir. Ao esgotar as tentativas devolve a última
/// resposta (ou relança a falha de rede). Nunca refaz 4xx de cliente (401, 403, 404...) nem cancelamentos.
/// Escolhido no lugar de um pacote de resiliência por ser pequeno e testável com <see cref="TimeProvider"/>.
/// </summary>
public sealed class ReintentoHandler : DelegatingHandler
{
    private readonly TimeProvider _relogio;

    public ReintentoHandler(
        TimeProvider relogio,
        int maximoDeReintentos = 4,
        TimeSpan? esperaInicial = null,
        TimeSpan? esperaMaximaPorTentativa = null,
        double fatorDeJitter = 0.25)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximoDeReintentos);
        ArgumentOutOfRangeException.ThrowIfNegative(fatorDeJitter);

        _relogio = relogio;
        MaximoDeReintentos = maximoDeReintentos;
        EsperaInicial = esperaInicial ?? TimeSpan.FromSeconds(1);
        EsperaMaximaPorTentativa = esperaMaximaPorTentativa ?? TimeSpan.FromSeconds(60);
        FatorDeJitter = fatorDeJitter;
    }

    public int MaximoDeReintentos { get; }

    public TimeSpan EsperaInicial { get; }

    public TimeSpan EsperaMaximaPorTentativa { get; }

    public double FatorDeJitter { get; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Uma HttpRequestMessage só pode ser enviada uma vez: guarda o corpo para refazer cópias.
        var corpo = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        for (var tentativa = 0; ; tentativa++)
        {
            var ultima = tentativa >= MaximoDeReintentos;
            using var copia = tentativa == 0 ? null : Copiar(request, corpo);

            HttpResponseMessage resposta;
            try
            {
                resposta = await base.SendAsync(copia ?? request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (!ultima)
            {
                await Task.Delay(Backoff(tentativa), _relogio, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (ultima || !Transitorio(resposta.StatusCode))
                return resposta;

            var espera = EsperaSugerida(resposta) ?? Backoff(tentativa);
            if (espera > EsperaMaximaPorTentativa)
                return resposta; // esperar tanto não compensa: o chamador vê o 429 e reporta

            resposta.Dispose();
            await Task.Delay(espera, _relogio, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool Transitorio(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            or (HttpStatusCode)529; // service_overload do Notion

    private TimeSpan? EsperaSugerida(HttpResponseMessage resposta)
    {
        var retryAfter = resposta.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta >= TimeSpan.Zero)
            return delta;

        if (retryAfter?.Date is { } data)
        {
            var espera = data - _relogio.GetUtcNow();
            return espera > TimeSpan.Zero ? espera : TimeSpan.Zero;
        }

        return null;
    }

    private TimeSpan Backoff(int tentativa)
    {
        var baseMs = EsperaInicial.TotalMilliseconds * Math.Pow(2, tentativa);
        var jitter = FatorDeJitter == 0 ? 0 : baseMs * FatorDeJitter * Random.Shared.NextDouble();
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }

    private static HttpRequestMessage Copiar(HttpRequestMessage original, byte[]? corpo)
    {
        var copia = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };

        foreach (var cabecalho in original.Headers)
            copia.Headers.TryAddWithoutValidation(cabecalho.Key, cabecalho.Value);

        if (corpo is not null)
        {
            copia.Content = new ByteArrayContent(corpo);
            foreach (var cabecalho in original.Content!.Headers)
                copia.Content.Headers.TryAddWithoutValidation(cabecalho.Key, cabecalho.Value);
        }

        return copia;
    }
}
