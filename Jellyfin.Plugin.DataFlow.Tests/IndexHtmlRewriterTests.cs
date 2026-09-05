using Jellyfin.Plugin.DataFlow.Web;
using Xunit;

namespace Jellyfin.Plugin.DataFlow.Tests;

public class IndexHtmlRewriterTests
{
    [Theory]
    [InlineData("/web/index.html", "")]
    [InlineData("/web/", "")]
    [InlineData("/web", "")]
    [InlineData("/WEB/Index.HTML", "")]
    [InlineData("/jellyfin/web/index.html", "/jellyfin")]
    [InlineData("/jellyfin/web/", "/jellyfin")]
    [InlineData("/a/b/web", "/a/b")]
    public void DetectsIndexPages(string path, string expectedPrefix)
    {
        Assert.True(IndexHtmlRewriter.TryGetIndexPrefix(path, out var prefix));
        Assert.Equal(expectedPrefix, prefix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/web/main.jellyfin.bundle.js")]
    [InlineData("/web/index.htm")]
    [InlineData("/webapp")]
    [InlineData("/System/Info")]
    public void IgnoresOtherPaths(string? path) => Assert.False(IndexHtmlRewriter.TryGetIndexPrefix(path, out _));

    [Fact]
    public void InjectsBeforeBodyClose()
    {
        var tag = IndexHtmlRewriter.BuildScriptTag("/jf", "1.2.3.4");
        var html = "<html><body><div>x</div></body></html>";
        var result = IndexHtmlRewriter.Inject(html, tag);
        Assert.Equal("<html><body><div>x</div>" + tag + "</body></html>", result);
        Assert.Contains("src=\"/jf/DataFlow/client.js?v=1.2.3.4\"", tag, System.StringComparison.Ordinal);
    }

    [Fact]
    public void InjectIsIdempotent()
    {
        var tag = IndexHtmlRewriter.BuildScriptTag(string.Empty, "1");
        var once = IndexHtmlRewriter.Inject("<body></body>", tag);
        var twice = IndexHtmlRewriter.Inject(once, tag);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void AppendsWhenNoBodyTag()
    {
        var tag = IndexHtmlRewriter.BuildScriptTag(string.Empty, "1");
        Assert.Equal("<p>x</p>" + tag, IndexHtmlRewriter.Inject("<p>x</p>", tag));
    }
}
