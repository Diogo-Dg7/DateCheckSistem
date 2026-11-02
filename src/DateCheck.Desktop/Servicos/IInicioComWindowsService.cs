namespace DateCheck.Desktop.Servicos;

/// <summary>Liga/desliga a abertura automática do aplicativo quando o usuário entra no Windows.</summary>
public interface IInicioComWindowsService
{
    bool Habilitado { get; }

    void Definir(bool habilitado);

    /// <summary>Disparado quando o estado muda (permite manter menu da bandeja e caixa de seleção em sincronia).</summary>
    event Action? Alterado;
}

/// <summary>Acesso à chave HKCU\Software\Microsoft\Windows\CurrentVersion\Run (abstraído para testes).</summary>
public interface IRegistroDeInicializacao
{
    string? Ler(string nome);

    void Gravar(string nome, string valor);

    void Remover(string nome);
}
