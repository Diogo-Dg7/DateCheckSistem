using Microsoft.Win32;

namespace DateCheck.Desktop.Servicos;

public sealed class InicioComWindowsService : IInicioComWindowsService
{
    public const string NomeDoValor = "DateCheckSistem";

    private readonly IRegistroDeInicializacao _registro;
    private readonly Func<string> _caminhoDoExecutavel;

    public InicioComWindowsService(IRegistroDeInicializacao registro, Func<string> caminhoDoExecutavel)
    {
        _registro = registro;
        _caminhoDoExecutavel = caminhoDoExecutavel;
    }

    public event Action? Alterado;

    public bool Habilitado => _registro.Ler(NomeDoValor) is not null;

    public void Definir(bool habilitado)
    {
        if (habilitado)
            _registro.Gravar(NomeDoValor, ComandoDeInicio());
        else
            _registro.Remover(NomeDoValor);

        Alterado?.Invoke();
    }

    /// <summary>
    /// Se a opção está ligada mas o registro aponta para outro caminho (o app foi movido ou reinstalado),
    /// atualiza para o executável atual. Não liga nada que o usuário não tenha ligado.
    /// </summary>
    public void CorrigirCaminhoSeNecessario()
    {
        var atual = _registro.Ler(NomeDoValor);
        if (atual is not null && !string.Equals(atual, ComandoDeInicio(), StringComparison.OrdinalIgnoreCase))
            _registro.Gravar(NomeDoValor, ComandoDeInicio());
    }

    // Aspas protegem caminhos com espaço (ex.: C:\Users\Fulano de Tal\...).
    private string ComandoDeInicio() => $"\"{_caminhoDoExecutavel()}\"";
}

public sealed class RegistroDeInicializacaoDoWindows : IRegistroDeInicializacao
{
    private const string ChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Ler(string nome)
    {
        using var chave = Registry.CurrentUser.OpenSubKey(ChaveRun);
        return chave?.GetValue(nome) as string;
    }

    public void Gravar(string nome, string valor)
    {
        using var chave = Registry.CurrentUser.CreateSubKey(ChaveRun, writable: true);
        chave.SetValue(nome, valor, RegistryValueKind.String);
    }

    public void Remover(string nome)
    {
        using var chave = Registry.CurrentUser.OpenSubKey(ChaveRun, writable: true);
        chave?.DeleteValue(nome, throwOnMissingValue: false);
    }
}
