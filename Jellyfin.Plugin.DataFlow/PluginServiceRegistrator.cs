using Jellyfin.Plugin.DataFlow.Metrics;
using Jellyfin.Plugin.DataFlow.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.DataFlow;

/// <summary>
/// Registers the plugin's services with the host's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ThroughputStore>();
        serviceCollection.AddSingleton<IStartupFilter, DataFlowStartupFilter>();
        serviceCollection.AddHostedService<SamplerHostedService>();
    }
}
