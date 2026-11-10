using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DateCheck.Application.Interfaces;

namespace DateCheck.Integracoes.Segredos;

/// <summary>
/// Cofre em arquivos protegidos pelo DPAPI do Windows (escopo <see cref="DataProtectionScope.CurrentUser"/>): só o
/// mesmo usuário do Windows, na mesma máquina, consegue ler. Um arquivo por segredo, na pasta informada
/// (<c>%LOCALAPPDATA%\DateCheck\segredos</c>, ou <c>DateCheck-Dev</c> em desenvolvimento).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed partial class CofreDpapi : ICofreDeSegredos
{
    private const string Extensao = ".segredo";

    // Entropia fixa do aplicativo: outro programa do mesmo usuário não decifra os arquivos chamando o DPAPI sem ela.
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("DateCheckSistem.Cofre.v1");

    private readonly string _pasta;

    public CofreDpapi(string pasta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pasta);
        _pasta = pasta;
    }

    public string? Ler(string nome)
    {
        var caminho = CaminhoDe(nome);

        try
        {
            if (!File.Exists(caminho))
                return null;

            var protegido = File.ReadAllBytes(caminho);
            var bytes = ProtectedData.Unprotect(protegido, Entropia, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // Corrompido, de outro usuário ou ilegível: tratado como "sem segredo". Quem usa pede para reconectar.
            return null;
        }
    }

    public void Gravar(string nome, string valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        var caminho = CaminhoDe(nome);

        Directory.CreateDirectory(_pasta);

        var protegido = ProtectedData.Protect(Encoding.UTF8.GetBytes(valor), Entropia, DataProtectionScope.CurrentUser);

        // Grava num temporário e troca: uma queda no meio não corrompe o segredo existente.
        var temporario = caminho + ".tmp";
        File.WriteAllBytes(temporario, protegido);
        File.Move(temporario, caminho, overwrite: true);
    }

    public void Apagar(string nome)
    {
        var caminho = CaminhoDe(nome);
        if (File.Exists(caminho))
            File.Delete(caminho);
    }

    // O nome vira nome de arquivo: restringir impede "..\" e caminhos absolutos.
    private string CaminhoDe(string nome)
    {
        if (string.IsNullOrEmpty(nome) || !NomeValido().IsMatch(nome))
            throw new ArgumentException("Nome de segredo inválido.", nameof(nome));

        return Path.Combine(_pasta, nome + Extensao);
    }

    [GeneratedRegex("^[a-z0-9._-]{1,64}$")]
    private static partial Regex NomeValido();
}
