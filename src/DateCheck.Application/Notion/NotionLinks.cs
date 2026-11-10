using System.Text.RegularExpressions;

namespace DateCheck.Application.Notion;

/// <summary>Descobre o id de um banco do Notion a partir do link copiado do navegador (ou do próprio id).</summary>
public static partial class NotionLinks
{
    /// <summary>
    /// Aceita o link do banco (<c>https://www.notion.so/ws/Titulo-&lt;32 hex&gt;?v=&lt;visão&gt;</c> ou, no formato atual do
    /// Notion, <c>https://app.notion.com/p/&lt;32 hex&gt;?pvs=...</c>), o id com ou sem hífens e links <c>notion.site</c>. O id da visão (<c>?v=</c>) nunca é confundido com o do banco.
    /// Retorna o id no formato com hífens, ou nulo se não houver um id válido.
    /// </summary>
    public static string? ExtrairIdDoBanco(string? linkOuId)
    {
        if (string.IsNullOrWhiteSpace(linkOuId))
            return null;

        var texto = linkOuId.Trim();

        // Só o caminho do link importa: a query (?v=...) e o fragmento (#...) trazem outros ids.
        if (Uri.TryCreate(texto, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is not ("http" or "https") || !EhDominioDoNotion(uri.Host))
                return null;

            texto = uri.AbsolutePath;
        }
        else
        {
            var corte = texto.IndexOfAny(['?', '#']);
            if (corte >= 0)
                texto = texto[..corte];
        }

        var ultimo = IdCom32Hex().Matches(texto).LastOrDefault();
        if (ultimo is null)
            return Hifenizado().IsMatch(texto.Trim('/')) ? texto.Trim('/').ToLowerInvariant() : null;

        var hex = ultimo.Groups[1].Value.ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    private static bool EhDominioDoNotion(string host) =>
        host.Equals("notion.so", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".notion.so", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".notion.site", StringComparison.OrdinalIgnoreCase)
        // O botão "Copiar link" atual do Notion gera https://app.notion.com/p/<id>?pvs=...
        || host.Equals("notion.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".notion.com", StringComparison.OrdinalIgnoreCase);

    // 32 hex precedidos de início, "/" ou "-" e seguidos de fim, "/" ou "-": o final de "Titulo-<id>".
    [GeneratedRegex("(?<![0-9a-fA-F])([0-9a-fA-F]{32})(?![0-9a-fA-F])")]
    private static partial Regex IdCom32Hex();

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex Hifenizado();
}
