using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.DataFlow.Web;

/// <summary>
/// Rewrites the web client's <c>index.html</c> response in flight so it loads the plugin's
/// client script. Nothing is written to disk.
/// </summary>
public class ScriptInjectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ScriptInjectionMiddleware> _logger;
    private readonly string _version;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScriptInjectionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="logger">The logger.</param>
    public ScriptInjectionMiddleware(RequestDelegate next, ILogger<ScriptInjectionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _version = typeof(ScriptInjectionMiddleware).Assembly.GetName().Version?.ToString() ?? "0";
    }

    /// <summary>
    /// Processes a request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            || !Plugin.CurrentConfiguration.EnableInjection
            || !IndexHtmlRewriter.TryGetIndexPrefix(context.Request.Path.Value, out string prefix))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // We need the full, uncompressed document: no ranges, no compression, no 304s.
        var reqHeaders = context.Request.Headers;
        reqHeaders.Remove(HeaderNames.AcceptEncoding);
        reqHeaders.Remove(HeaderNames.Range);
        reqHeaders.Remove(HeaderNames.IfRange);
        reqHeaders.Remove(HeaderNames.IfNoneMatch);
        reqHeaders.Remove(HeaderNames.IfModifiedSince);

        Stream originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context).ConfigureAwait(false);

            context.Response.Body = originalBody;
            buffer.Position = 0;

            if (context.Response.StatusCode == StatusCodes.Status200OK
                && IsHtml(context.Response.ContentType)
                && string.IsNullOrEmpty(context.Response.Headers.ContentEncoding))
            {
                string html = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                string rewritten = IndexHtmlRewriter.Inject(html, IndexHtmlRewriter.BuildScriptTag(prefix, _version));
                byte[] bytes = Encoding.UTF8.GetBytes(rewritten);

                var resHeaders = context.Response.Headers;
                resHeaders.Remove(HeaderNames.ETag);
                resHeaders.Remove(HeaderNames.LastModified);
                resHeaders.Remove(HeaderNames.AcceptRanges);
                resHeaders.CacheControl = "no-cache";
                context.Response.ContentLength = bytes.Length;

                await originalBody.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            // Not something we rewrite: pass the buffered response through unchanged.
            if (buffer.Length > 0)
            {
                await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "DataFlow: failed to rewrite index.html; response passed through");
            context.Response.Body = originalBody;
            if (!context.Response.HasStarted && buffer.Length > 0)
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
            }
        }
    }

    private static bool IsHtml(string? contentType)
        => contentType is not null && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);
}
