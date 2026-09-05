using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.DataFlow.Metrics;
using Jellyfin.Plugin.DataFlow.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Jellyfin.Plugin.DataFlow.Tests;

/// <summary>
/// Runs the real middleware in a TestServer with a fake downstream app that behaves like
/// Jellyfin's static file / media handlers.
/// </summary>
public sealed class MiddlewareIntegrationTests : IAsyncLifetime
{
    private const string IndexHtml = "<!doctype html><html><head></head><body><div id=\"app\"></div></body></html>";
    private static readonly byte[] MediaBytes = new byte[100_000];

    private readonly ThroughputStore _store = new(30);
    private IHost? _host;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s =>
                {
                    s.AddSingleton(_store);
                    s.AddSingleton<IStartupFilter, DataFlowStartupFilter>();
                });
                web.Configure(app =>
                {
                    app.Run(async ctx =>
                    {
                        var path = ctx.Request.Path.Value ?? string.Empty;
                        if (path.EndsWith("index.html", StringComparison.Ordinal) || path.EndsWith("/web/", StringComparison.Ordinal))
                        {
                            var bytes = Encoding.UTF8.GetBytes(IndexHtml);
                            ctx.Response.ContentType = "text/html";
                            ctx.Response.ContentLength = bytes.Length;
                            ctx.Response.Headers.ETag = "\"orig\"";
                            ctx.Response.Headers.LastModified = "Mon, 01 Jan 2024 00:00:00 GMT";
                            await ctx.Response.Body.WriteAsync(bytes);
                        }
                        else if (path.Contains("/stream", StringComparison.Ordinal))
                        {
                            ctx.Response.ContentType = "video/mp4";
                            // Exercise the SendFileAsync path used by PhysicalFileResult.
                            var tmp = Path.GetTempFileName();
                            await File.WriteAllBytesAsync(tmp, MediaBytes);
                            try
                            {
                                await ctx.Response.SendFileAsync(tmp);
                            }
                            finally
                            {
                                File.Delete(tmp);
                            }
                        }
                        else if (path.Contains("/hls1/", StringComparison.Ordinal))
                        {
                            ctx.Response.ContentType = "video/mp2t";
                            await ctx.Response.Body.WriteAsync(MediaBytes.AsMemory(0, 40_000));
                            await ctx.Response.Body.WriteAsync(MediaBytes.AsMemory(0, 2_000));
                        }
                        else
                        {
                            ctx.Response.ContentType = "application/json";
                            await ctx.Response.WriteAsync("{\"ok\":true}");
                        }
                    });
                });
            })
            .StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [Fact]
    public async Task IndexHtml_IsRewrittenWithCorrectLengthAndNoValidators()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/web/index.html");
        req.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, br");
        req.Headers.TryAddWithoutValidation("If-None-Match", "\"orig\"");
        var res = await _client!.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("<script src=\"/DataFlow/client.js?v=", body, StringComparison.Ordinal);
        Assert.EndsWith("</script></body></html>", body, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetByteCount(body), res.Content.Headers.ContentLength);
        Assert.Null(res.Headers.ETag);
        Assert.Null(res.Content.Headers.LastModified);
    }

    [Fact]
    public async Task IndexHtml_UnderBaseUrl_UsesPrefixedScriptUrl()
    {
        var body = await _client!.GetStringAsync("/jellyfin/web/");
        Assert.Contains("src=\"/jellyfin/DataFlow/client.js", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonIndex_PassesThroughUntouched()
    {
        var res = await _client!.GetAsync("/System/Info");
        Assert.Equal("{\"ok\":true}", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DirectPlay_SendFile_IsCounted()
    {
        var res = await _client!.GetAsync("/Videos/item1/stream.mp4?static=true&deviceId=dev-a");
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal(MediaBytes.Length, bytes.Length);

        Assert.True(_store.TryGet("dev-a", out var counters));
        counters!.Drain(1);
        Assert.Equal(new long[] { MediaBytes.Length }, counters.Down.Latest(1));
        Assert.True(counters.Up.Latest(1)[0] > 0);
    }

    [Fact]
    public async Task HlsSegment_WithHeaderDeviceId_IsCounted()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/Videos/item1/hls1/main/0.ts");
        req.Headers.TryAddWithoutValidation("Authorization", "MediaBrowser Client=\"web\", DeviceId=\"dev-b\"");
        var res = await _client!.SendAsync(req);
        Assert.Equal(42_000, (await res.Content.ReadAsByteArrayAsync()).Length);

        Assert.True(_store.TryGet("dev-b", out var counters));
        counters!.Drain(1);
        Assert.Equal(new long[] { 42_000 }, counters.Down.Latest(1));
    }

    [Fact]
    public async Task ApiRequest_CountsUploadOnly()
    {
        var res = await _client!.GetAsync("/Sessions/Playing/Progress?deviceId=dev-c");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(_store.TryGet("dev-c", out var counters));
        counters!.Drain(1);
        Assert.Equal(0, counters.Down.Latest(1)[0]);
        Assert.True(counters.Up.Latest(1)[0] > 0);
    }

    [Fact]
    public async Task RequestWithoutDeviceId_IsNotTracked()
    {
        await _client!.GetAsync("/Videos/item1/stream.mp4");
        Assert.False(_store.TryGet(string.Empty, out _));
        Assert.Equal(0, _store.DeviceCount);
    }
}
