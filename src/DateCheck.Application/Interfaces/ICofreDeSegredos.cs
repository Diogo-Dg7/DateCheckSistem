namespace DateCheck.Application.Interfaces;

/// <summary>
/// Guarda segredos (token do Notion, tokens OAuth do Google, client_secret.json) fora do Git, do
/// <c>appsettings.json</c> e do banco. Segredos nunca vão para log, mensagem de exceção ou tela.
/// </summary>
public interface ICofreDeSegredos
{
    /// <summary>Retorna nulo se o segredo não existe ou está ilegível (arquivo corrompido, outro usuário do Windows).</summary>
    string? Ler(string nome);

    /// <summary>Cria ou substitui o segredo. <paramref name="nome"/>: letras minúsculas, dígitos, ponto, hífen e sublinhado.</summary>
    void Gravar(string nome, string valor);

    /// <summary>Apaga o segredo; não faz nada se ele não existe.</summary>
    void Apagar(string nome);
}
