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

    /// <summary>Agrupa cópias criadas juntas (ex.: "repetir"); nulo para agendamentos avulsos.</summary>
    public Guid? SerieId { get; private set; }

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

    /// <summary>
    /// Cria um novo agendamento pendente com o mesmo título, descrição e duração, começando em <paramref name="novoInicio"/>.
    /// A cópia é independente do original e só pertence a uma série se <paramref name="serieId"/> for informado.
    /// </summary>
    public Agendamento CopiarPara(DateTime novoInicio, Guid? serieId = null)
    {
        var copia = new Agendamento(Titulo, novoInicio, novoInicio + (Fim - Inicio), Descricao);
        copia.SerieId = serieId;
        return copia;
    }

    /// <summary>Associa o agendamento a uma série; não permite trocar por outra série.</summary>
    public void AssociarSerie(Guid serieId)
    {
        if (serieId == Guid.Empty)
            throw new DomainException("O identificador da série é inválido.");

        if (SerieId is { } atual && atual != serieId)
            throw new DomainException("O agendamento já pertence a outra série.");

        SerieId = serieId;
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
