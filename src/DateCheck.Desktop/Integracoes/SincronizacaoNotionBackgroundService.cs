using DateCheck.Application.Servicos;
using DateCheck.Desktop.Configuracoes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DateCheck.Desktop.Integracoes;

/// <summary>
/// Em segundo plano, sincroniza o Notion na abertura do app e depois a cada N minutos (5 a 60, padrão 10, das
/// preferências), com <see cref="PeriodicTimer"/> sobre o <see cref="TimeProvider"/> injetado. Sem Notion
/// configurado nada acontece (o serviço de sincronização só devolve "não configurado"). O "Sincronizar agora" das
/// Configurações chama o mesmo <see cref="ISincronizacaoExternaService"/>, que nunca roda duas ao mesmo tempo.
/// </summary>
public sealed class SincronizacaoNotionBackgroundService : BackgroundService
{
    private readonly ISincronizacaoExternaService _sincronizacao;
    private readonly IPreferenciasService _preferencias;
    private readonly TimeProvider _relogio;
    private readonly ILogger<SincronizacaoNotionBackgroundService> _logger;

    public SincronizacaoNotionBackgroundService(
        ISincronizacaoExternaService sincronizacao,
        IPreferenciasService preferencias,
        TimeProvider relogio,
        ILogger<SincronizacaoNotionBackgroundService> logger)
    {
        _sincronizacao = sincronizacao;
        _preferencias = preferencias;
        _relogio = relogio;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(IntervaloAtual(), _relogio);

        // Mudar o intervalo nas Configurações vale na hora, sem esperar o tique antigo.
        void AoAlterarPreferencias()
        {
            try
            {
                timer.Period = IntervaloAtual();
            }
            catch (ObjectDisposedException)
            {
                // Encerrando.
            }
        }

        _preferencias.Alterado += AoAlterarPreferencias;
        try
        {
            // Já na abertura: o app pode ter sido aberto depois de dias parado.
            await SincronizarSemFalharAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SincronizarSemFalharAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do aplicativo.
        }
        finally
        {
            _preferencias.Alterado -= AoAlterarPreferencias;
        }
    }

    private TimeSpan IntervaloAtual() => TimeSpan.FromMinutes(_preferencias.Atual.Notion.IntervaloEmMinutos);

    private async Task SincronizarSemFalharAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _sincronizacao.SincronizarAsync(manual: false, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // O serviço já converte as falhas em estado; isto é só uma rede de segurança para o laço não morrer.
            _logger.LogError(ex, "Falha inesperada no ciclo de sincronização com o Notion.");
        }
    }
}
