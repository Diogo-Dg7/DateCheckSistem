namespace DateCheck.Desktop.Servicos;

/// <summary>Abre um link no navegador padrão; existe para os ViewModels serem testáveis sem abrir processos.</summary>
public interface IAbridorDeLinks
{
    /// <summary>Retorna false se o link não é um endereço http(s) válido ou se o sistema não conseguiu abri-lo.</summary>
    bool Abrir(string url);
}
