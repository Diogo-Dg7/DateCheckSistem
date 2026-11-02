namespace DateCheck.Desktop.Configuracoes;

/// <summary>Preferências do usuário, guardadas em JSON em %LOCALAPPDATA%\DateCheck.</summary>
public sealed record Preferencias
{
    public const int AntecedenciaPadraoEmMinutos = 10;
    public const int AntecedenciaMaximaEmMinutos = 24 * 60;

    public static Preferencias Padrao { get; } = new();

    /// <summary>Quantos minutos antes do início de um compromisso o aviso (toast) aparece.</summary>
    public int MinutosAntes { get; init; } = AntecedenciaPadraoEmMinutos;

    public bool EhValida => MinutosAntes is >= 0 and <= AntecedenciaMaximaEmMinutos;
}
