using Jellyfin.Plugin.DataFlow.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Jellyfin.Plugin.DataFlow.Tests;

public class DeviceIdResolverTests
{
    [Theory]
    [InlineData("MediaBrowser Client=\"Jellyfin Web\", Device=\"Chrome\", DeviceId=\"abc123\", Version=\"10.11.0\"", "abc123")]
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

    [Fact]
    public void QueryTakesPrecedenceOverHeader()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString("?DeviceId=fromquery&api_key=k");
        ctx.Request.Headers.Authorization = "MediaBrowser DeviceId=\"fromheader\"";
        Assert.Equal("fromquery", DeviceIdResolver.Resolve(ctx.Request));
    }

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
