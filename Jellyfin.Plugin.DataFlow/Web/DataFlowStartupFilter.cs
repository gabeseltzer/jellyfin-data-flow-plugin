using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.DataFlow.Web;

/// <summary>
/// Adds the plugin's middleware at the outermost position of the ASP.NET pipeline.
/// </summary>
/// <remarks>
/// Startup filters wrap the host's <c>Configure</c>, so middleware added here runs before
/// everything Jellyfin registers (static files, routing, compression). That is what allows
/// the index.html rewrite to see the uncompressed response and the byte counter to see
/// every media response.
/// </remarks>
public class DataFlowStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<ScriptInjectionMiddleware>();
            app.UseMiddleware<ByteCountingMiddleware>();
            next(app);
        };
    }
}
