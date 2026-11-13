using DateCheck.Application.Interfaces;
using DateCheck.Application.Notion;

namespace DateCheck.Desktop.Configuracoes;

/// <summary>
/// Guarda a configuração não secreta do Notion no <c>preferencias.json</c>, preservando o resto das preferências
/// (antecedência dos avisos, intervalo de sincronização).
/// </summary>
public sealed class ConfiguracaoNotionDasPreferencias : IConfiguracaoNotion
{
    private readonly IPreferenciasService _preferencias;

    public ConfiguracaoNotionDasPreferencias(IPreferenciasService preferencias)
    {
        _preferencias = preferencias;
    }

    public ConfiguracaoNotion? Obter()
    {
        var n = _preferencias.Atual.Notion;
        if (string.IsNullOrWhiteSpace(n.DatabaseId)
            || string.IsNullOrWhiteSpace(n.DataSourceId)
            || string.IsNullOrWhiteSpace(n.PropriedadeData))
            return null;

        return new ConfiguracaoNotion(n.DatabaseId, n.DataSourceId, n.PropriedadeData, ObterConclusao(n));
    }

    public void Salvar(ConfiguracaoNotion configuracao)
    {
        var atual = _preferencias.Atual;
        _preferencias.Salvar(atual with
        {
            Notion = atual.Notion with
            {
                DatabaseId = configuracao.DatabaseId,
                DataSourceId = configuracao.DataSourceId,
                PropriedadeData = configuracao.PropriedadeData,
                ConclusaoTipo = configuracao.Conclusao?.Tipo switch
                {
                    TipoDeConclusaoNotion.Checkbox => "checkbox",
                    TipoDeConclusaoNotion.Status => "status",
                    _ => null
                },
                ConclusaoPropriedade = configuracao.Conclusao?.Propriedade,
                ConclusaoValor = configuracao.Conclusao?.ValorConcluido
            }
        });
    }

    public void Limpar()
    {
        var atual = _preferencias.Atual;
        _preferencias.Salvar(atual with
        {
            // O intervalo escolhido é preferência do usuário, não da conexão: sobrevive à desconexão.
            Notion = new NotionPreferencias { IntervaloEmMinutos = atual.Notion.IntervaloEmMinutos }
        });
    }

    private static ConclusaoNotion? ObterConclusao(NotionPreferencias n)
    {
        if (string.IsNullOrWhiteSpace(n.ConclusaoPropriedade))
            return null;

        return n.ConclusaoTipo switch
        {
            "checkbox" => new ConclusaoNotion(n.ConclusaoPropriedade, TipoDeConclusaoNotion.Checkbox),
            "status" => new ConclusaoNotion(n.ConclusaoPropriedade, TipoDeConclusaoNotion.Status, n.ConclusaoValor),
            _ => null
        };
    }
}
