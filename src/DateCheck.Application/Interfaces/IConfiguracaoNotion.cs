using DateCheck.Application.Notion;

namespace DateCheck.Application.Interfaces;

/// <summary>Onde fica a configuração não secreta do Notion (no app: <c>preferencias.json</c>).</summary>
public interface IConfiguracaoNotion
{
    ConfiguracaoNotion? Obter();

    void Salvar(ConfiguracaoNotion configuracao);

    void Limpar();
}
