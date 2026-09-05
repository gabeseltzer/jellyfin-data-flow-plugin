using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Jellyfin.Plugin.DataFlow.Web;

/// <summary>
/// Finds the client device id on a request.
/// </summary>
public static class DeviceIdResolver
{
    private const string AuthHeader = "Authorization";
    private const string LegacyAuthHeader = "X-Emby-Authorization";

    /// <summary>
    /// Looks for a device id in the <c>deviceId</c> query parameter, then in the Jellyfin
    /// authorization header (<c>MediaBrowser ..., DeviceId="..."</c>).
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The device id, or <c>null</c>.</returns>
    public static string? Resolve(HttpRequest request)
    {
        if (request.Query.TryGetValue("deviceId", out StringValues q) && !StringValues.IsNullOrEmpty(q))
        {
            return q[0];
        }

        if (request.Headers.TryGetValue(AuthHeader, out StringValues auth) && TryParseDeviceId(auth.ToString(), out var id))
        {
            return id;
        }

        if (request.Headers.TryGetValue(LegacyAuthHeader, out auth) && TryParseDeviceId(auth.ToString(), out id))
        {
            return id;
        }

        return null;
    }

    /// <summary>
    /// Extracts <c>DeviceId="..."</c> from a Jellyfin authorization header value.
    /// </summary>
    /// <param name="header">The header value.</param>
    /// <param name="deviceId">The device id when present.</param>
    /// <returns><c>true</c> when found.</returns>
    public static bool TryParseDeviceId(string? header, out string? deviceId)
    {
        deviceId = null;
        if (string.IsNullOrEmpty(header))
        {
            return false;
        }

        int idx = header.IndexOf("DeviceId=", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return false;
        }

        ReadOnlySpan<char> rest = header.AsSpan(idx + "DeviceId=".Length).TrimStart();
        if (rest.Length == 0)
        {
            return false;
        }

        if (rest[0] == '"')
        {
            rest = rest[1..];
            int end = rest.IndexOf('"');
            if (end < 0)
            {
                return false;
            }

            rest = rest[..end];
        }
        else
        {
            int end = rest.IndexOf(',');
            if (end >= 0)
            {
                rest = rest[..end];
            }
        }

        if (rest.Length == 0)
        {
            return false;
        }

        deviceId = Uri.UnescapeDataString(rest.ToString());
        return true;
    }
}
