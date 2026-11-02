using DateCheck.Application.Dtos;

namespace DateCheck.Desktop.Notificacoes;

/// <summary>Mostra um aviso ao usuário (toast do Windows na implementação real).</summary>
public interface INotificador
{
    /// <param name="minutosRestantes">Minutos até o início (0 = começando agora).</param>
    void Notificar(AgendamentoDto agendamento, int minutosRestantes);
}
