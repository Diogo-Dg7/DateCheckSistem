namespace DateCheck.Application.Notion;

/// <summary>Tipos de propriedade do Notion de que o app precisa. <c>Outro</c> cobre todos os demais.</summary>
public static class TiposDePropriedadeNotion
{
    public const string Titulo = "title";
    public const string Data = "date";
    public const string Checkbox = "checkbox";
    public const string Status = "status";
}

/// <param name="Tipo">Valor do tipo na API ("title", "date", "checkbox", "status", "select"...).</param>
/// <param name="Opcoes">Nomes das opções (só para "status").</param>
public sealed record PropriedadeNotion(string Nome, string Tipo, IReadOnlyList<string> Opcoes)
{
    public PropriedadeNotion(string nome, string tipo) : this(nome, tipo, [])
    {
    }
}

/// <summary>Um data source de um banco do Notion (desde a API 2025-09-03 um banco tem um ou mais).</summary>
public sealed record FonteDeDadosNotion(string Id, string Nome, IReadOnlyList<PropriedadeNotion> Propriedades)
{
    public IEnumerable<PropriedadeNotion> PropriedadesDeData =>
        Propriedades.Where(p => p.Tipo == TiposDePropriedadeNotion.Data);

    /// <summary>Propriedades que podem indicar "concluído": checkbox ou status.</summary>
    public IEnumerable<PropriedadeNotion> PropriedadesDeConclusao =>
        Propriedades.Where(p => p.Tipo is TiposDePropriedadeNotion.Checkbox or TiposDePropriedadeNotion.Status);
}

public sealed record EstruturaDoBancoNotion(
    string DatabaseId, string Titulo, IReadOnlyList<FonteDeDadosNotion> FontesDeDados);

public enum TipoDeConclusaoNotion
{
    /// <summary>Checkbox marcado = concluído.</summary>
    Checkbox,

    /// <summary>Status com a opção <c>ValorConcluido</c> = concluído.</summary>
    Status
}

/// <param name="ValorConcluido">Para Status: nome da opção que significa concluído. Ignorado em Checkbox.</param>
public sealed record ConclusaoNotion(string Propriedade, TipoDeConclusaoNotion Tipo, string? ValorConcluido = null);

/// <summary>Configuração NÃO secreta da integração (o token fica no cofre).</summary>
public sealed record ConfiguracaoNotion(
    string DatabaseId,
    string DataSourceId,
    string PropriedadeData,
    ConclusaoNotion? Conclusao = null);
