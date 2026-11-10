namespace DateCheck.Application.Excecoes;

public enum TipoDeFalhaExterna
{
    /// <summary>Token recusado (401): é preciso reconectar.</summary>
    NaoAutorizado,

    /// <summary>Token válido, mas sem permissão (403) ou recurso não compartilhado com a integração (404).</summary>
    SemAcesso,

    /// <summary>429 que não cedeu depois de todas as tentativas.</summary>
    LimiteExcedido,

    /// <summary>Sem rede, API fora do ar ou 5xx.</summary>
    Indisponivel,

    /// <summary>A API respondeu algo que o app não entende (JSON inesperado, propriedade que mudou de tipo).</summary>
    RespostaInvalida,

    /// <summary>A consulta terminou sem entregar todos os itens (ex.: limite de 10 mil resultados do Notion).</summary>
    Incompleta
}

/// <summary>
/// Falha ao falar com um serviço externo. A mensagem é para o usuário e NUNCA contém token, cabeçalhos ou
/// qualquer segredo. Como é uma <see cref="AplicacaoException"/>, as telas a exibem sem derrubar o app.
/// </summary>
public sealed class FonteExternaException : AplicacaoException
{
    public FonteExternaException(TipoDeFalhaExterna tipo, string mensagem, Exception? causa = null)
        : base(mensagem)
    {
        Tipo = tipo;
        CausaOriginal = causa;
    }

    public TipoDeFalhaExterna Tipo { get; }

    /// <summary>Só para log; não é exibida. Pode ser nula.</summary>
    public Exception? CausaOriginal { get; }

    /// <summary>Uma lista parcial nunca pode alimentar a remoção: em qualquer falha, nada é removido.</summary>
    public bool ExigeReconexao => Tipo == TipoDeFalhaExterna.NaoAutorizado;
}
