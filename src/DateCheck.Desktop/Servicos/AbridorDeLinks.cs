using System.ComponentModel;
using System.Diagnostics;

namespace DateCheck.Desktop.Servicos;

/// <summary>
/// Abre links no navegador padrão. O endereço vem de um serviço externo (ex.: Notion), então só http e https
/// são aceitos: nunca se entrega ao shell um caminho de arquivo, um protocolo registrado ou um comando.
/// </summary>
public sealed class AbridorDeLinks : IAbridorDeLinks
{
    public bool Abrir(string url)
    {
        if (!EhLinkWebValido(url, out var endereco))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(endereco.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public static bool EhLinkWebValido(string? url, out Uri endereco)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            endereco = uri;
            return true;
        }

        endereco = null!;
        return false;
    }
}
