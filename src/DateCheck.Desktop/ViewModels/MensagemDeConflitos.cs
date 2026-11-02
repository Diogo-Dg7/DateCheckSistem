using System.Text;
using DateCheck.Application.Dtos;

namespace DateCheck.Desktop.ViewModels;

internal static class MensagemDeConflitos
{
    private const int MaximoListado = 5;

    /// <summary>Texto de confirmação do tipo "conflita com... Salvar mesmo assim?".</summary>
    public static string Montar(IReadOnlyList<AgendamentoDto> conflitos, string pergunta = "Salvar mesmo assim?")
    {
        var texto = new StringBuilder("Este horário conflita com:\n");

        foreach (var conflito in conflitos.Take(MaximoListado))
            texto.AppendLine($"• {Periodo(conflito)}  {conflito.Titulo}");

        if (conflitos.Count > MaximoListado)
            texto.AppendLine($"• e mais {conflitos.Count - MaximoListado}...");

        texto.AppendLine().Append(pergunta);
        return texto.ToString();
    }

    public static string Periodo(AgendamentoDto agendamento) =>
        agendamento.Inicio.Date == agendamento.Fim.Date
            ? $"{agendamento.Inicio:dd/MM/yyyy HH:mm}–{agendamento.Fim:HH:mm}"
            : $"{agendamento.Inicio:dd/MM/yyyy HH:mm}–{agendamento.Fim:dd/MM/yyyy HH:mm}";
}
