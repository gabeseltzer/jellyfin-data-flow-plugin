using System;
using System.Text;

namespace Jellyfin.Plugin.DataFlow.Web;

/// <summary>
/// Pure helpers for deciding whether a request is the web client's index page and for
/// inserting the plugin's script tag into it. Kept free of ASP.NET types so it is unit testable.
/// </summary>
public static class IndexHtmlRewriter
{
    private const string ScriptMarker = "data-dataflow";

    /// <summary>
    /// Determines whether a request path is the web client's index page and, if so, returns the
    /// URL prefix in front of <c>/web</c> (the server BaseUrl as seen by the client).
    /// </summary>
    /// <param name="path">The request path, e.g. <c>/jellyfin/web/index.html</c>.</param>
    /// <param name="prefix">Receives the path prefix before <c>/web</c>, e.g. <c>/jellyfin</c> or an empty string.</param>
    /// <returns><c>true</c> when the path is an index page.</returns>
    public static bool TryGetIndexPrefix(string? path, out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        ReadOnlySpan<char> p = path.AsSpan();
        if (p.Length > 1 && p[^1] == '/')
        {
            p = p[..^1];
        }

        if (p.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
        {
            p = p[..^"/index.html".Length];
        }

        if (!p.EndsWith("/web", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        prefix = p[..^"/web".Length].ToString();
        return true;
    }

    /// <summary>
    /// Builds the script tag that loads the client.
    /// </summary>
    /// <param name="prefix">Path prefix in front of the API (server BaseUrl).</param>
    /// <param name="version">Cache-busting version string.</param>
    /// <returns>The HTML for the script tag.</returns>
    public static string BuildScriptTag(string prefix, string version)
    {
        return $"<script src=\"{prefix}/DataFlow/client.js?v={Uri.EscapeDataString(version)}\" defer {ScriptMarker}></script>";
    }

    /// <summary>
    /// Inserts the script tag before <c>&lt;/body&gt;</c> (or at the end when there is no body tag).
    /// Idempotent: a document that already carries the marker is returned unchanged.
    /// </summary>
    /// <param name="html">The original HTML.</param>
    /// <param name="scriptTag">The script tag to insert.</param>
    /// <returns>The rewritten HTML.</returns>
    public static string Inject(string html, string scriptTag)
    {
        if (html.Contains(ScriptMarker, StringComparison.Ordinal))
        {
            return html;
        }

        int idx = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return html + scriptTag;
        }

        var sb = new StringBuilder(html.Length + scriptTag.Length);
        sb.Append(html, 0, idx);
        sb.Append(scriptTag);
        sb.Append(html, idx, html.Length - idx);
        return sb.ToString();
    }
}
