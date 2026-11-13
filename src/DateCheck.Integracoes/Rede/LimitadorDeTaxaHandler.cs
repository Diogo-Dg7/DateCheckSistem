namespace DateCheck.Integracoes.Rede;

/// <summary>
/// Controla o ritmo das requisições para respeitar o limite da API (o Notion aceita em média 3 por segundo).
/// Cada requisição reserva o próximo horário livre; as que chegam juntas saem uma depois da outra. É compartilhado
/// (singleton) entre os handlers que o <c>IHttpClientFactory</c> recria de tempos em tempos. Usa o
/// <see cref="TimeProvider"/> injetado para os testes não esperarem de verdade.
/// </summary>
public sealed class LimitadorDeTaxa
{
    private readonly TimeProvider _relogio;
    private readonly TimeSpan _intervalo;
    private readonly object _trava = new();

    private DateTimeOffset _proximaSaida = DateTimeOffset.MinValue;

    public LimitadorDeTaxa(TimeProvider relogio, TimeSpan intervaloMinimo)
    {
        if (intervaloMinimo < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(intervaloMinimo));

        _relogio = relogio;
        _intervalo = intervaloMinimo;
    }

    public async Task AguardarVezAsync(CancellationToken cancellationToken)
    {
        TimeSpan espera;
        lock (_trava)
        {
            var agora = _relogio.GetUtcNow();
            var saida = agora > _proximaSaida ? agora : _proximaSaida;
            espera = saida - agora;
            _proximaSaida = saida + _intervalo;
        }

        if (espera > TimeSpan.Zero)
            await Task.Delay(espera, _relogio, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Passa cada requisição pelo <see cref="LimitadorDeTaxa"/> antes de enviá-la.</summary>
public sealed class LimitadorDeTaxaHandler : DelegatingHandler
{
    private readonly LimitadorDeTaxa _limitador;

    public LimitadorDeTaxaHandler(LimitadorDeTaxa limitador)
    {
        _limitador = limitador;
    }

    public LimitadorDeTaxaHandler(TimeProvider relogio, TimeSpan intervaloMinimo)
        : this(new LimitadorDeTaxa(relogio, intervaloMinimo))
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _limitador.AguardarVezAsync(cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
