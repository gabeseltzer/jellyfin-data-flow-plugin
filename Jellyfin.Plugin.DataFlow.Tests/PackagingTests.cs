using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.DataFlow.Tests;

/// <summary>
/// Guards the packaging metadata that decides which servers are offered which build. Jellyfin
/// loads a plugin only when its targetAbi matches the server's major version, so a drift here
/// ships a DLL that 12.0 refuses to load, or strands 10.11 servers without an update.
/// </summary>
public class PackagingTests
{
    private const string Jellyfin12Abi = "12.0.0.0";
    private const string Jellyfin1011Abi = "10.11.0.0";

    private static readonly string Root = FindRepoRoot();

    // Name and Id are constant overrides; skip BasePlugin's constructor, which needs real app paths.
    private static readonly Plugin PluginInstance = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));

    [Fact]
    public void AssemblyTargetsJellyfin12Abi()
    {
        Assert.Equal(Jellyfin12Abi, BuildYaml("targetAbi"));
        Assert.Equal("net10.0", BuildYaml("framework"));
        Assert.Equal(Jellyfin12Abi, MetaJson().GetProperty("targetAbi").GetString());
        Assert.Equal(CsprojVersion(), BuildYaml("version"));
        Assert.Equal(new Version(CsprojVersion()), typeof(Plugin).Assembly.GetName().Version);
    }

    [Fact]
    public void PluginNameMatchesPackagingAndIsASafeDirectoryName()
    {
        // 12.0 installs to "<name>_<version>" and rejects names that are not a single safe
        // path segment (InstallationManager.IsValidPackageDirectoryName).
        string name = PluginInstance.Name;
        Assert.Equal(name, BuildYaml("name"));
        Assert.Equal(name, MetaJson().GetProperty("name").GetString());
        Assert.Equal(name, ManifestPlugin().GetProperty("name").GetString());

        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.NotEqual(".", name);
        Assert.NotEqual("..", name);
        Assert.True(
            name.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0,
            $"'{name}' contains a character Jellyfin 12.0 rejects in package names");
    }

    [Fact]
    public void ManifestGuidMatchesPlugin()
    {
        var guid = PluginInstance.Id;
        Assert.Equal(guid, Guid.Parse(ManifestPlugin().GetProperty("guid").GetString()!));
        Assert.Equal(guid, Guid.Parse(MetaJson().GetProperty("guid").GetString()!));
        Assert.Equal(guid.ToString(), BuildYaml("guid"));
    }

    [Fact]
    public void ManifestOffersEachServerLineACompatibleBuild()
    {
        var versions = ManifestPlugin().GetProperty("versions").EnumerateArray().ToList();
        Assert.All(versions, v => Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", v.GetProperty("targetAbi").GetString()));

        // A 10.11 server must keep seeing a 10.11 build after the 12.0 release.
        Assert.Contains(versions, v => v.GetProperty("targetAbi").GetString() == Jellyfin1011Abi);

        // Once the current version is published, its entry must carry the 12.0 ABI.
        var current = versions.Where(v => v.GetProperty("version").GetString() == CsprojVersion()).ToList();
        Assert.All(current, v => Assert.Equal(Jellyfin12Abi, v.GetProperty("targetAbi").GetString()));
        Assert.True(current.Count <= 1, "duplicate manifest entries for " + CsprojVersion());
    }

    private static string CsprojVersion()
    {
        string csproj = File.ReadAllText(Path.Combine(Root, "Jellyfin.Plugin.DataFlow", "Jellyfin.Plugin.DataFlow.csproj"));
        return Regex.Match(csproj, "<AssemblyVersion>([^<]+)</AssemblyVersion>").Groups[1].Value;
    }

    private static string BuildYaml(string key)
    {
        string yaml = File.ReadAllText(Path.Combine(Root, "build.yaml"));
        var m = Regex.Match(yaml, "^" + Regex.Escape(key) + @":\s*""?([^""\r\n]*)""?\s*$", RegexOptions.Multiline);
        Assert.True(m.Success, $"build.yaml has no '{key}'");
        return m.Groups[1].Value;
    }

    private static JsonElement MetaJson() => ReadJson("meta.json");

    private static JsonElement ManifestPlugin() => ReadJson("manifest.json")[0];

    private static JsonElement ReadJson(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, file)));
        return doc.RootElement.Clone();
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Jellyfin.Plugin.DataFlow.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("repository root not found above " + AppContext.BaseDirectory);
    }
}
