namespace DateCheck.Application.Notion;

/// <summary>Casos de uso da aba Notion das Configurações. Falhas previsíveis são <c>AplicacaoException</c>.</summary>
public interface INotionService
{
    /// <summary>Há token e banco configurados.</summary>
    bool EstaConectado { get; }

    /// <summary>A configuração salva (não secreta), ou nulo.</summary>
    ConfiguracaoNotion? ConfiguracaoAtual { get; }

    /// <summary>
    /// Valida o token e o link e lista data sources e propriedades. Não grava nada.
    /// </summary>
    Task<EstruturaDoBancoNotion> InspecionarAsync(string token, string linkOuIdDoBanco, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida as escolhas contra a estrutura, guarda o token no cofre e a configuração nas preferências.
    /// Nada é gravado se alguma escolha for inválida.
    /// </summary>
    Task ConectarAsync(
        string token,
        EstruturaDoBancoNotion estrutura,
        string dataSourceId,
        string propriedadeData,
        ConclusaoNotion? conclusao,
        CancellationToken cancellationToken = default);

    /// <summary>"Conectado como …": confere token, acesso ao banco e se as propriedades escolhidas ainda existem.</summary>
    Task<string> TestarConexaoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Apaga o token do cofre e a configuração e remove os itens importados do Notion (só eles; nunca os locais).
    /// Retorna quantos itens foram removidos.
    /// </summary>
    Task<int> DesconectarAsync(CancellationToken cancellationToken = default);
}
