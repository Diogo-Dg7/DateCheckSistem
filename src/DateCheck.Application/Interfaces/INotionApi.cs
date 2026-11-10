using DateCheck.Application.Notion;
using DateCheck.Domain.Entidades;

namespace DateCheck.Application.Interfaces;

/// <summary>Cliente da API do Notion (implementado na camada Integracoes). Todas as falhas viram <c>FonteExternaException</c>.</summary>
public interface INotionApi
{
    /// <summary>Quem é a integração do token (nome do bot e do workspace), para "conectado como …". Valida o token.</summary>
    Task<string> ObterIdentidadeAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Banco, data sources e propriedades de cada um.</summary>
    Task<EstruturaDoBancoNotion> ObterEstruturaAsync(string token, string databaseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Todas as páginas do data source com a propriedade de data entre as duas datas (inclusive), já convertidas
    /// em agendamentos importados. Percorre todas as páginas ou falha. Páginas na lixeira/arquivadas e sem data
    /// não entram.
    /// </summary>
    Task<IReadOnlyList<Agendamento>> ConsultarAsync(
        string token, ConfiguracaoNotion configuracao, DateOnly de, DateOnly ate, CancellationToken cancellationToken = default);
}
