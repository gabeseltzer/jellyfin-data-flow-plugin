using Jellyfin.Plugin.DataFlow.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Jellyfin.Plugin.DataFlow.Tests;

public class DeviceIdResolverTests
{
    [Theory]
    [InlineData("MediaBrowser Client=\"Jellyfin Web\", Device=\"Chrome\", DeviceId=\"abc123\", Version=\"12.0.0\"", "abc123")]
    [InlineData("MediaBrowser DeviceId=\"with%20space\", Client=\"x\"", "with space")]
    [InlineData("MediaBrowser Client=\"x\", DeviceId=unquoted, Version=\"1\"", "unquoted")]
    [InlineData("MediaBrowser deviceid=\"lower\"", "lower")]
    public void ParsesDeviceIdFromAuthorizationHeader(string header, string expected)
    {
        Assert.True(DeviceIdResolver.TryParseDeviceId(header, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("MediaBrowser Client=\"x\", DeviceId=\"")]
    [InlineData("MediaBrowser DeviceId=\"\"")]
    public void RejectsHeadersWithoutDeviceId(string? header)
    {
        Assert.False(DeviceIdResolver.TryParseDeviceId(header, out _));
    }

    // Jellyfin 12.0's web client sends no auth header on media requests: <video> and hls.js
    // URLs carry DeviceId and ApiKey in the query (api_key is rejected by default in 12.0).
    [Theory]
    [InlineData("?static=true&deviceId=web-dev&MediaSourceId=m&ApiKey=k")]
    [InlineData("?DeviceId=web-dev&MediaSourceId=m&PlaySessionId=p&ApiKey=k")]
    [InlineData("?deviceid=web-dev")]
    public void ReadsDeviceIdFromQuery(string query)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString(query);
        Assert.Equal("web-dev", DeviceIdResolver.Resolve(ctx.Request));
    }

    [Fact]
    public void QueryTakesPrecedenceOverHeader()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString("?DeviceId=fromquery&ApiKey=k");
        ctx.Request.Headers.Authorization = "MediaBrowser DeviceId=\"fromheader\"";
        Assert.Equal("fromquery", DeviceIdResolver.Resolve(ctx.Request));
    }

    // X-Emby-Authorization is disabled by default in 12.0 but still honoured when the admin
    // turns EnableLegacyAuthorization back on, so older clients must still be attributed.
    [Fact]
    public void FallsBackToLegacyHeader()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Emby-Authorization"] = "MediaBrowser DeviceId=\"legacy\"";
        Assert.Equal("legacy", DeviceIdResolver.Resolve(ctx.Request));
    }

    [Fact]
    public void ReturnsNullWhenAbsent()
    {
        var ctx = new DefaultHttpContext();
        Assert.Null(DeviceIdResolver.Resolve(ctx.Request));
    }
}
