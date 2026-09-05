using System;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.DataFlow.Metrics;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.DataFlow.Web;

/// <summary>
/// Attributes response bytes of media routes (client download) and request bytes of any
/// request carrying a device id (client upload) to that device.
/// </summary>
public class ByteCountingMiddleware
{
    // Rough size of the request line and the headers a browser sends that we do not enumerate exactly.
    private const int RequestOverheadBytes = 40;

    private readonly RequestDelegate _next;
    private readonly ThroughputStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ByteCountingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="store">The throughput store.</param>
    public ByteCountingMiddleware(RequestDelegate next, ThroughputStore store)
    {
        _next = next;
        _store = store;
    }

    /// <summary>
    /// Processes a request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        string? deviceId = DeviceIdResolver.Resolve(context.Request);
        if (string.IsNullOrEmpty(deviceId))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        DeviceCounters counters = _store.GetOrCreate(deviceId, nowMs);
        counters.AddUp(EstimateRequestBytes(context.Request));

        if (!MediaRouteMatcher.IsMediaPath(context.Request.Path.Value))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Setting Response.Body replaces IHttpResponseBodyFeature with a stream-backed one,
        // so SendFileAsync (used for direct play) is routed through our stream as well.
        Stream originalBody = context.Response.Body;
        var counting = new CountingStream(originalBody, counters);
        context.Response.Body = counting;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static long EstimateRequestBytes(HttpRequest request)
    {
        long total = RequestOverheadBytes
            + request.Method.Length
            + (request.Path.Value?.Length ?? 0)
            + (request.QueryString.Value?.Length ?? 0);
        foreach (var header in request.Headers)
        {
            total += header.Key.Length + 4; // ": " and CRLF
            foreach (var value in header.Value)
            {
                total += value?.Length ?? 0;
            }
        }

        return total + (request.ContentLength ?? 0);
    }
}
