using DateCheck.Application.Dtos;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DateCheck.Desktop.Notificacoes;

/// <summary>
/// Toast nativo do Windows pelo <c>AppNotificationManager</c> (Windows App SDK), a API recomendada pela
/// Microsoft para WPF não empacotado. Clicar no toast abre a tela Hoje.
/// </summary>
public sealed class AppNotificacaoNotificador : INotificador
{
    public const string ArgumentoAcao = "acao";
    public const string AcaoAbrirHoje = "abrir-hoje";

    public void Notificar(AgendamentoDto agendamento, int minutosRestantes)
    {
        var quando = minutosRestantes <= 0
            ? $"Começa agora, às {agendamento.Inicio:HH:mm}"
            : $"Começa em {minutosRestantes} min, às {agendamento.Inicio:HH:mm}";

        var builder = new AppNotificationBuilder()
            .AddArgument(ArgumentoAcao, AcaoAbrirHoje)
            .AddText(agendamento.Titulo)
            .AddText(quando);

        if (!string.IsNullOrWhiteSpace(agendamento.Descricao))
            builder.AddText(agendamento.Descricao);

        AppNotificationManager.Default.Show(builder.BuildNotification());
    }
}

/// <summary>
/// Registro do aplicativo para receber a ativação dos toasts (clique) e limpeza ao sair.
/// Se o app estiver fechado quando o usuário clica num toast da Central de Ações, o Windows o inicia
/// e o clique chega aqui logo após o <see cref="Iniciar"/>.
/// </summary>
public sealed class GerenciadorDeToast
{
    /// <param name="aoClicar">Chamado em thread do pool: quem usa deve voltar para a thread da interface.</param>
    public void Iniciar(Action aoClicar)
    {
        var gerenciador = AppNotificationManager.Default;

        gerenciador.NotificationInvoked += (_, argumentos) =>
        {
            if (argumentos.Arguments.TryGetValue(AppNotificacaoNotificador.ArgumentoAcao, out var acao)
                && acao == AppNotificacaoNotificador.AcaoAbrirHoje)
                aoClicar();
        };

        // Precisa vir depois de assinar o evento, senão uma ativação pendente se perderia.
        gerenciador.Register();
    }

    /// <summary>Ao sair: remove os toasts antigos da Central de Ações e desfaz o registro de ativação.</summary>
    public void Encerrar()
    {
        var gerenciador = AppNotificationManager.Default;

        try
        {
            gerenciador.RemoveAllAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Limpeza de cortesia: não impede o encerramento.
        }

        try
        {
            gerenciador.Unregister();
        }
        catch (Exception)
        {
            // Idem.
        }
    }
}
