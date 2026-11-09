using DateCheck.Domain.Enums;
using DateCheck.Domain.Exceptions;

namespace DateCheck.Domain.Entidades;

public class Agendamento
{
    public const int TamanhoMaximoTitulo = 200;
    public const int TamanhoMaximoDescricao = 2000;
    public const int TamanhoMaximoIdExterno = 200;
    public const int TamanhoMaximoUrlExterna = 2000;

    public int Id { get; private set; }
    public string Titulo { get; private set; } = string.Empty;
    public string? Descricao { get; private set; }
    public DateTime Inicio { get; private set; }
    public DateTime Fim { get; private set; }
    public StatusAgendamento Status { get; private set; }

    /// <summary>Agrupa cópias criadas juntas (ex.: "repetir"); nulo para agendamentos avulsos.</summary>
    public Guid? SerieId { get; private set; }

    /// <summary>Onde o item nasceu. Itens externos só mudam por <see cref="AtualizarDaOrigem"/>.</summary>
    public OrigemAgendamento Origem { get; private set; } = OrigemAgendamento.Local;

    /// <summary>Identificador do item na origem; obrigatório se a origem não é local e proibido se é.</summary>
    public string? IdExterno { get; private set; }

    /// <summary>Link para abrir o item na origem (ex.: a página do Notion).</summary>
    public string? UrlExterna { get; private set; }

    /// <summary>
    /// Compromisso de dia inteiro: <see cref="Inicio"/> é 00:00 do primeiro dia e <see cref="Fim"/> é 00:00 do
    /// dia seguinte ao último.
    /// </summary>
    public bool DiaInteiro { get; private set; }

    public bool EhExterno => Origem != OrigemAgendamento.Local;

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

    /// <summary>Cria um item vindo de uma origem externa (somente leitura no DateCheck).</summary>
    public static Agendamento Importar(
        OrigemAgendamento origem,
        string idExterno,
        string? url,
        string titulo,
        string? descricao,
        DateTime inicio,
        DateTime fim,
        bool diaInteiro,
        StatusAgendamento status)
    {
        if (origem == OrigemAgendamento.Local || !Enum.IsDefined(origem))
            throw new DomainException("A origem de um item importado deve ser externa.");

        var idValido = ValidarIdExterno(idExterno);
        var urlValida = ValidarUrlExterna(url);
        var tituloValido = ValidarTitulo(titulo);
        var descricaoValida = ValidarDescricao(descricao);
        ValidarPeriodo(inicio, fim, diaInteiro);
        ValidarStatus(status);

        return new Agendamento
        {
            Origem = origem,
            IdExterno = idValido,
            UrlExterna = urlValida,
            Titulo = tituloValido,
            Descricao = descricaoValida,
            Inicio = inicio,
            Fim = fim,
            DiaInteiro = diaInteiro,
            Status = status
        };
    }

    /// <summary>
    /// Único caminho para alterar um item externo: copia o estado atual da origem (inclusive o status).
    /// Origem e <see cref="IdExterno"/> nunca mudam.
    /// </summary>
    public void AtualizarDaOrigem(
        string? url,
        string titulo,
        string? descricao,
        DateTime inicio,
        DateTime fim,
        bool diaInteiro,
        StatusAgendamento status)
    {
        if (!EhExterno)
            throw new DomainException("Compromissos locais não são atualizados por uma origem externa.");

        var urlValida = ValidarUrlExterna(url);
        var tituloValido = ValidarTitulo(titulo);
        var descricaoValida = ValidarDescricao(descricao);
        ValidarPeriodo(inicio, fim, diaInteiro);
        ValidarStatus(status);

        UrlExterna = urlValida;
        Titulo = tituloValido;
        Descricao = descricaoValida;
        Inicio = inicio;
        Fim = fim;
        DiaInteiro = diaInteiro;
        Status = status;
    }

    public void Concluir()
    {
        GarantirLocal();

        if (Status == StatusAgendamento.Concluido)
            return;

        if (Status == StatusAgendamento.Cancelado)
            throw new DomainException("Não é possível concluir um agendamento cancelado.");

        Status = StatusAgendamento.Concluido;
    }

    public void Cancelar()
    {
        GarantirLocal();

        if (Status == StatusAgendamento.Cancelado)
            return;

        if (Status == StatusAgendamento.Concluido)
            throw new DomainException("Não é possível cancelar um agendamento concluído.");

        Status = StatusAgendamento.Cancelado;
    }

    /// <summary>Devolve um agendamento concluído ou cancelado ao estado pendente.</summary>
    public void Reabrir()
    {
        GarantirLocal();

        Status = StatusAgendamento.Pendente;
    }

    public void AlterarDetalhes(string titulo, string? descricao)
    {
        GarantirLocal();
        GarantirPendente("alterar");

        var tituloValido = ValidarTitulo(titulo);
        var descricaoValida = ValidarDescricao(descricao);

        Titulo = tituloValido;
        Descricao = descricaoValida;
    }

    public void Reagendar(DateTime inicio, DateTime fim)
    {
        GarantirLocal();
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
        GarantirLocal();

        var copia = new Agendamento(Titulo, novoInicio, novoInicio + (Fim - Inicio), Descricao);
        copia.SerieId = serieId;
        return copia;
    }

    /// <summary>Associa o agendamento a uma série; não permite trocar por outra série.</summary>
    public void AssociarSerie(Guid serieId)
    {
        GarantirLocal();

        if (serieId == Guid.Empty)
            throw new DomainException("O identificador da série é inválido.");

        if (SerieId is { } atual && atual != serieId)
            throw new DomainException("O agendamento já pertence a outra série.");

        SerieId = serieId;
    }

    /// <summary>Itens externos são somente leitura: só a origem os altera (<see cref="AtualizarDaOrigem"/>).</summary>
    private void GarantirLocal()
    {
        if (EhExterno)
            throw new DomainException($"Este compromisso vem do {Origem.Nome()}; edite-o lá.");
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

    private static void ValidarPeriodo(DateTime inicio, DateTime fim, bool diaInteiro = false)
    {
        if (fim <= inicio)
            throw new DomainException("O fim deve ser posterior ao início.");

        if (diaInteiro && (inicio.TimeOfDay != TimeSpan.Zero || fim.TimeOfDay != TimeSpan.Zero))
            throw new DomainException("Um compromisso de dia inteiro deve começar e terminar à meia-noite.");
    }

    private static string ValidarIdExterno(string? idExterno)
    {
        if (string.IsNullOrWhiteSpace(idExterno))
            throw new DomainException("O identificador externo é obrigatório para itens importados.");

        var idLimpo = idExterno.Trim();
        if (idLimpo.Length > TamanhoMaximoIdExterno)
            throw new DomainException($"O identificador externo deve ter no máximo {TamanhoMaximoIdExterno} caracteres.");

        return idLimpo;
    }

    private static string? ValidarUrlExterna(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var urlLimpa = url.Trim();
        if (urlLimpa.Length > TamanhoMaximoUrlExterna)
            throw new DomainException($"O link externo deve ter no máximo {TamanhoMaximoUrlExterna} caracteres.");

        return urlLimpa;
    }

    private static void ValidarStatus(StatusAgendamento status)
    {
        if (!Enum.IsDefined(status))
            throw new DomainException("O status informado é inválido.");
    }
}
