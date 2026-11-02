using DateCheck.Application.Dtos;
using DateCheck.Domain.Enums;

namespace DateCheck.Desktop.ViewModels;

/// <summary>Um agendamento como a tela o enxerga: texto pronto para exibir e quais ações fazem sentido.</summary>
public sealed class AgendamentoItemViewModel
{
    public AgendamentoItemViewModel(AgendamentoDto dto)
    {
        Dto = dto;
    }

    public AgendamentoDto Dto { get; }

    public int Id => Dto.Id;

    public string Titulo => Dto.Titulo;

    public string Descricao => Dto.Descricao ?? string.Empty;

    public bool TemDescricao => !string.IsNullOrEmpty(Dto.Descricao);

    public StatusAgendamento Status => Dto.Status;

    public bool Atrasado => Dto.Atrasado;

    public bool Proximo => Dto.Proximo;

    public bool EstaCancelado => Status == StatusAgendamento.Cancelado;

    public string StatusTexto => Status switch
    {
        StatusAgendamento.Concluido => "Concluído",
        StatusAgendamento.Cancelado => "Cancelado",
        _ => "Pendente"
    };

    /// <summary>"Próximo", "Atrasado" ou vazio.</summary>
    public string Marcador => Proximo ? "Próximo" : Atrasado ? "Atrasado" : string.Empty;

    public bool TemMarcador => Marcador.Length > 0;

    public string Horario => FormatarHorario(Dto.Inicio, Dto.Fim);

    public bool PodeConcluir => Status == StatusAgendamento.Pendente;

    public bool PodeCancelar => Status == StatusAgendamento.Pendente;

    public bool PodeEditar => Status == StatusAgendamento.Pendente;

    public bool PodeReabrir => Status != StatusAgendamento.Pendente;

    public static string FormatarHorario(DateTime inicio, DateTime fim) =>
        inicio.Date == fim.Date
            ? $"{inicio:HH:mm} – {fim:HH:mm}"
            : $"{inicio:dd/MM HH:mm} – {fim:dd/MM HH:mm}";
}
