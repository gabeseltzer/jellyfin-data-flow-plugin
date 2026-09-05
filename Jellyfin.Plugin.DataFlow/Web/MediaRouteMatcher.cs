using System;

namespace Jellyfin.Plugin.DataFlow.Web;

/// <summary>
/// Decides which request paths carry media bytes that should count as client download.
/// </summary>
public static class MediaRouteMatcher
{
    /// <summary>
    /// Returns <c>true</c> for media stream, HLS playlist/segment and subtitle routes under
    /// <c>/Videos</c> or <c>/Audio</c>, regardless of any BaseUrl prefix.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <returns>Whether the response body should be counted.</returns>
    public static bool IsMediaPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        ReadOnlySpan<char> p = path.AsSpan();
        int idx = p.IndexOf("/Videos/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            idx = p.IndexOf("/Audio/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return false;
            }

            p = p[(idx + "/Audio/".Length)..];
        }
        else
        {
            p = p[(idx + "/Videos/".Length)..];
        }

        // p is now "{id}/rest..."
        int slash = p.IndexOf('/');
        if (slash <= 0 || slash == p.Length - 1)
        {
            return false;
        }

        ReadOnlySpan<char> rest = p[(slash + 1)..];
        int nextSlash = rest.IndexOf('/');
        ReadOnlySpan<char> first = nextSlash < 0 ? rest : rest[..nextSlash];

        if (first.Equals("stream", StringComparison.OrdinalIgnoreCase)
            || first.StartsWith("stream.", StringComparison.OrdinalIgnoreCase)
            || first.Equals("master.m3u8", StringComparison.OrdinalIgnoreCase)
            || first.Equals("main.m3u8", StringComparison.OrdinalIgnoreCase)
            || first.Equals("live.m3u8", StringComparison.OrdinalIgnoreCase)
            || first.Equals("hls1", StringComparison.OrdinalIgnoreCase)
            || first.Equals("hls", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // /Videos/{id}/{mediaSourceId}/Subtitles/...
        if (nextSlash > 0)
        {
            ReadOnlySpan<char> second = rest[(nextSlash + 1)..];
            int thirdSlash = second.IndexOf('/');
            if (thirdSlash > 0)
            {
                second = second[..thirdSlash];
            }

            if (second.Equals("Subtitles", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
