namespace DateCheck.Desktop.Configuracoes;

/// <summary>
/// Preferências do usuário, guardadas em JSON na pasta de dados do ambiente (%LOCALAPPDATA%\DateCheck).
/// Aqui só ficam configurações NÃO secretas; tokens e senhas vão para o cofre (DPAPI).
/// </summary>
public sealed record Preferencias
{
    public const int AntecedenciaPadraoEmMinutos = 10;
    public const int AntecedenciaMaximaEmMinutos = 24 * 60;

    public static Preferencias Padrao { get; } = new();

    /// <summary>Quantos minutos antes do início de um compromisso o aviso (toast) aparece.</summary>
    public int MinutosAntes { get; init; } = AntecedenciaPadraoEmMinutos;

    /// <summary>Banco, propriedades escolhidas e intervalo da integração com o Notion.</summary>
    public NotionPreferencias Notion { get; init; } = new();

    public bool EhValida => MinutosAntes is >= 0 and <= AntecedenciaMaximaEmMinutos && Notion.EhValida;
}

/// <summary>Configuração não secreta do Notion (o token fica no cofre).</summary>
public sealed record NotionPreferencias
{
    public const int IntervaloPadraoEmMinutos = 10;
    public const int IntervaloMinimoEmMinutos = 5;
    public const int IntervaloMaximoEmMinutos = 60;

    /// <summary>De quanto em quanto tempo o Notion é consultado em segundo plano (5 a 60 minutos).</summary>
    public int IntervaloEmMinutos { get; init; } = IntervaloPadraoEmMinutos;

    public string? DatabaseId { get; init; }

    public string? DataSourceId { get; init; }

    public string? PropriedadeData { get; init; }

    /// <summary>"checkbox" ou "status"; nulo se nenhuma propriedade indica concluído.</summary>
    public string? ConclusaoTipo { get; init; }

    public string? ConclusaoPropriedade { get; init; }

    /// <summary>Para "status": o valor que significa concluído.</summary>
    public string? ConclusaoValor { get; init; }

    public bool EhValida => IntervaloEmMinutos is >= IntervaloMinimoEmMinutos and <= IntervaloMaximoEmMinutos;
}
