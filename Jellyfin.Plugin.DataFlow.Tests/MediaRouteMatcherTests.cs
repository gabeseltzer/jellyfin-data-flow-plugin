using Jellyfin.Plugin.DataFlow.Web;
using Xunit;

namespace Jellyfin.Plugin.DataFlow.Tests;

public class MediaRouteMatcherTests
{
    [Theory]
    [InlineData("/Videos/abc/stream")]
    [InlineData("/Videos/abc/stream.mp4")]
    [InlineData("/videos/ABC/Stream.mkv")]
    [InlineData("/Videos/abc/master.m3u8")]
    [InlineData("/Videos/abc/main.m3u8")]
    [InlineData("/Videos/abc/live.m3u8")]
    [InlineData("/Videos/abc/hls1/main/0.ts")]
    [InlineData("/Videos/abc/hls1/main/12.mp4")]
    [InlineData("/Videos/abc/hls/playlist/stream.m3u8")]
    [InlineData("/Audio/abc/stream")]
    [InlineData("/Audio/abc/stream.aac")]
    [InlineData("/Audio/abc/hls1/main/3.ts")]
    [InlineData("/Audio/abc/master.m3u8")]
    [InlineData("/Videos/abc/def/Subtitles/2/0/Stream.vtt")]
    [InlineData("/Videos/abc/def/Subtitles/2/subtitles.m3u8")]
    [InlineData("/jellyfin/Videos/abc/stream.mp4")]
    [InlineData("/base/url/Audio/abc/hls1/main/1.ts")]
    public void MatchesMediaRoutes(string path) => Assert.True(MediaRouteMatcher.IsMediaPath(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/Videos")]
    [InlineData("/Videos/")]
    [InlineData("/Videos/abc")]
    [InlineData("/Videos/abc/")]
    [InlineData("/Videos/abc/Trickplay/320/0.jpg")]
    [InlineData("/Videos/abc/AdditionalParts")]
    [InlineData("/Videos/ActiveEncodings")]
    [InlineData("/Items/abc/Images/Primary")]
    [InlineData("/Items/abc/PlaybackInfo")]
    [InlineData("/Sessions/Playing/Progress")]
    [InlineData("/Audio/abc/Lyrics")]
    [InlineData("/web/index.html")]
    public void RejectsOtherRoutes(string? path) => Assert.False(MediaRouteMatcher.IsMediaPath(path));
}
