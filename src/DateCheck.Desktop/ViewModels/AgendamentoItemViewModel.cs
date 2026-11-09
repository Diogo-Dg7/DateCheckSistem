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

    public bool DiaInteiro => Dto.DiaInteiro;

    /// <summary>Itens vindos de fora (Notion) são somente leitura: só a origem os altera.</summary>
    public bool EhExterno => Dto.EhExterno;

    /// <summary>Nome da origem para o selo ("Notion"); vazio nos itens locais.</summary>
    public string Selo => EhExterno ? Dto.Origem.Nome() : string.Empty;

    public bool TemSelo => EhExterno;

    public string Horario => DiaInteiro ? FormatarDiaInteiro(Dto.Inicio, Dto.Fim) : FormatarHorario(Dto.Inicio, Dto.Fim);

    public bool PodeConcluir => !EhExterno && Status == StatusAgendamento.Pendente;

    public bool PodeCancelar => !EhExterno && Status == StatusAgendamento.Pendente;

    public bool PodeEditar => !EhExterno && Status == StatusAgendamento.Pendente;

    public bool PodeDuplicar => !EhExterno;

    public bool PodeReabrir => !EhExterno && Status != StatusAgendamento.Pendente;

    public bool PodeAbrirNaOrigem => EhExterno && !string.IsNullOrWhiteSpace(Dto.UrlExterna);

    /// <summary>"Abrir no Notion".</summary>
    public string TextoAbrirNaOrigem => $"Abrir no {Dto.Origem.Nome()}";

    /// <summary>"Dia inteiro", com o intervalo quando ocupa mais de um dia (o fim é o início do dia seguinte ao último).</summary>
    public static string FormatarDiaInteiro(DateTime inicio, DateTime fim)
    {
        var ultimoDia = fim.Date.AddDays(-1);
        return ultimoDia > inicio.Date
            ? $"Dia inteiro ({inicio:dd/MM} – {ultimoDia:dd/MM})"
            : "Dia inteiro";
    }

    public static string FormatarHorario(DateTime inicio, DateTime fim) =>
        inicio.Date == fim.Date
            ? $"{inicio:HH:mm} – {fim:HH:mm}"
            : $"{inicio:dd/MM HH:mm} – {fim:dd/MM HH:mm}";
}
