using DateCheck.Application.Servicos;
using DateCheck.Desktop.Configuracoes;
using DateCheck.Domain.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DateCheck.Desktop.Notificacoes;

/// <summary>
/// Em segundo plano, procura periodicamente os compromissos pendentes que começam em até X minutos e avisa
/// uma única vez por par (Id, Início). Nunca avisa de compromissos que já começaram (por exemplo, ao voltar
/// da suspensão do notebook). Reagendar um compromisso gera um novo aviso, porque o início muda.
/// </summary>
public sealed class NotificacaoService : BackgroundService
{
    public static readonly TimeSpan IntervaloDeVerificacao = TimeSpan.FromSeconds(30);

    private readonly IAgendamentoService _agendamentos;
    private readonly INotificador _notificador;
    private readonly IPreferenciasService _preferencias;
    private readonly TimeProvider _relogio;
    private readonly ILogger<NotificacaoService> _logger;

    // Já avisados (chave = Id + Início). Em memória: ao reabrir o app, um compromisso ainda dentro da
    // janela de antecedência é avisado de novo, o que é desejável (o usuário pode ter perdido o toast).
    private readonly HashSet<(int Id, DateTime Inicio)> _jaAvisados = [];

    public NotificacaoService(
        IAgendamentoService agendamentos,
        INotificador notificador,
        IPreferenciasService preferencias,
        TimeProvider relogio,
        ILogger<NotificacaoService> logger)
    {
        _agendamentos = agendamentos;
        _notificador = notificador;
        _preferencias = preferencias;
        _relogio = relogio;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(IntervaloDeVerificacao, _relogio);

        try
        {
            // Já na abertura: o app pode ter sido aberto a minutos de um compromisso.
            await VerificarSemFalharAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
                await VerificarSemFalharAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do aplicativo.
        }
    }

    /// <summary>Uma passada de verificação (público para os testes).</summary>
    public async Task VerificarAsync(CancellationToken cancellationToken = default)
    {
        var agora = _relogio.GetLocalNow().DateTime;
        var limite = agora.AddMinutes(_preferencias.Atual.MinutosAntes);

        // +1 s só para o período da consulta (que exclui o limite superior); o filtro abaixo é exato.
        var candidatos = await _agendamentos.ListarPeriodoAsync(agora, limite.AddSeconds(1), cancellationToken);

        foreach (var agendamento in candidatos)
        {
            // Só pendentes que AINDA NÃO começaram e começam dentro da antecedência.
            if (agendamento.Status != StatusAgendamento.Pendente
                || agendamento.Inicio < agora
                || agendamento.Inicio > limite)
                continue;

            var chave = (agendamento.Id, agendamento.Inicio);
            if (_jaAvisados.Contains(chave))
                continue;

            var minutos = (int)Math.Ceiling((agendamento.Inicio - agora).TotalMinutes);
            try
            {
                _notificador.Notificar(agendamento, minutos);
                _jaAvisados.Add(chave);
            }
            catch (Exception ex)
            {
                // Não marca como avisado: a próxima passada tenta de novo.
                _logger.LogWarning(ex, "Não foi possível exibir a notificação do compromisso {Id}.", agendamento.Id);
            }
        }

        _jaAvisados.RemoveWhere(chave => chave.Inicio < agora);
    }

    private async Task VerificarSemFalharAsync(CancellationToken cancellationToken)
    {
        try
        {
            await VerificarAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Banco indisponível, por exemplo: a próxima passada tenta de novo.
            _logger.LogWarning(ex, "Falha ao procurar compromissos para notificar.");
        }
    }
}
