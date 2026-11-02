using DateCheck.Domain.Enums;
using DateCheck.Domain.Exceptions;

namespace DateCheck.Domain.Entidades;

public class Agendamento
{
    public const int TamanhoMaximoTitulo = 200;
    public const int TamanhoMaximoDescricao = 2000;

    public int Id { get; private set; }
    public string Titulo { get; private set; } = string.Empty;
    public string? Descricao { get; private set; }
    public DateTime Inicio { get; private set; }
    public DateTime Fim { get; private set; }
    public StatusAgendamento Status { get; private set; }

    // Construtor sem parâmetros reservado para a materialização pelo ORM.
    private Agendamento()
    {
    }

    public Agendamento(string titulo, DateTime inicio, DateTime fim, string? descricao = null)
    {
        var tituloValido = ValidarTitulo(titulo);
        var descricaoValida = ValidarDescricao(descricao);
        ValidarPeriodo(inicio, fim);

        Titulo = tituloValido;
        Descricao = descricaoValida;
        Inicio = inicio;
        Fim = fim;
        Status = StatusAgendamento.Pendente;
    }

    public void Concluir()
    {
        if (Status == StatusAgendamento.Concluido)
            return;

        if (Status == StatusAgendamento.Cancelado)
            throw new DomainException("Não é possível concluir um agendamento cancelado.");

        Status = StatusAgendamento.Concluido;
    }

    public void Cancelar()
    {
        if (Status == StatusAgendamento.Cancelado)
            return;

        if (Status == StatusAgendamento.Concluido)
            throw new DomainException("Não é possível cancelar um agendamento concluído.");

        Status = StatusAgendamento.Cancelado;
    }

    public void AlterarDetalhes(string titulo, string? descricao)
    {
        GarantirPendente("alterar");

        var tituloValido = ValidarTitulo(titulo);
        var descricaoValida = ValidarDescricao(descricao);

        Titulo = tituloValido;
        Descricao = descricaoValida;
    }

    public void Reagendar(DateTime inicio, DateTime fim)
    {
        GarantirPendente("reagendar");
        ValidarPeriodo(inicio, fim);

        Inicio = inicio;
        Fim = fim;
    }

    private void GarantirPendente(string acao)
    {
        if (Status != StatusAgendamento.Pendente)
            throw new DomainException($"Não é possível {acao} um agendamento {Status.ToString().ToLowerInvariant()}.");
    }

    private static string ValidarTitulo(string? titulo)
    {
        if (string.IsNullOrWhiteSpace(titulo))
            throw new DomainException("O título é obrigatório.");

        var tituloLimpo = titulo.Trim();
        if (tituloLimpo.Length > TamanhoMaximoTitulo)
            throw new DomainException($"O título deve ter no máximo {TamanhoMaximoTitulo} caracteres.");

        return tituloLimpo;
    }

    private static string? ValidarDescricao(string? descricao)
    {
        if (string.IsNullOrWhiteSpace(descricao))
            return null;

        var descricaoLimpa = descricao.Trim();
        if (descricaoLimpa.Length > TamanhoMaximoDescricao)
            throw new DomainException($"A descrição deve ter no máximo {TamanhoMaximoDescricao} caracteres.");

        return descricaoLimpa;
    }

    private static void ValidarPeriodo(DateTime inicio, DateTime fim)
    {
        if (fim <= inicio)
            throw new DomainException("O fim deve ser posterior ao início.");
    }
}
