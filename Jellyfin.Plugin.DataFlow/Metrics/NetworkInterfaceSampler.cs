using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace Jellyfin.Plugin.DataFlow.Metrics;

/// <summary>
/// Reads cumulative byte counters from the host's network interfaces and turns them into
/// per-tick deltas.
/// </summary>
public sealed class NetworkInterfaceSampler
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private NetworkInterface[] _interfaces = [];
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private long _lastSent = -1;
    private long _lastReceived = -1;
    private string[] _filter = [];

    /// <summary>
    /// Lists the names of all interfaces that are candidates for inclusion.
    /// </summary>
    /// <returns>Interface names with their operational status.</returns>
    public static IReadOnlyList<(string Name, string Description, bool Up)> ListInterfaces()
    {
        var result = new List<(string, string, bool)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            result.Add((nic.Name, nic.Description, nic.OperationalStatus == OperationalStatus.Up));
        }

        return result;
    }

    /// <summary>
    /// Reads the interfaces and returns the bytes sent/received since the previous call.
    /// The first call (and the first call after the filter changes) returns zeros.
    /// </summary>
    /// <param name="includeNames">Interface names to include; empty for all up, non-loopback interfaces.</param>
    /// <param name="now">Current time.</param>
    /// <returns>Delta bytes sent and received.</returns>
    public (long Sent, long Received) Sample(string[] includeNames, DateTimeOffset now)
    {
        bool filterChanged = !SameFilter(includeNames);
        if (filterChanged || now - _lastRefresh > RefreshInterval)
        {
            _filter = (string[])includeNames.Clone();
            _interfaces = Select(includeNames);
            _lastRefresh = now;
        }

        long sent = 0;
        long received = 0;
        foreach (var nic in _interfaces)
        {
            try
            {
                var stats = nic.GetIPStatistics();
                sent += stats.BytesSent;
                received += stats.BytesReceived;
            }
            catch (NetworkInformationException)
            {
                // Interface went away between refreshes; it will be dropped at the next refresh.
            }
        }

        (long, long) delta = (0, 0);
        if (!filterChanged && _lastSent >= 0)
        {
            delta = (Math.Max(0, sent - _lastSent), Math.Max(0, received - _lastReceived));
        }

        _lastSent = sent;
        _lastReceived = received;
        return delta;
    }

    private bool SameFilter(string[] names)
    {
        if (names.Length != _filter.Length)
        {
            return false;
        }

        for (int i = 0; i < names.Length; i++)
        {
            if (!string.Equals(names[i], _filter[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static NetworkInterface[] Select(string[] includeNames)
    {
        var list = new List<NetworkInterface>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            if (includeNames.Length == 0)
            {
                if (nic.OperationalStatus == OperationalStatus.Up)
                {
                    list.Add(nic);
                }
            }
            else if (Array.Exists(includeNames, n => string.Equals(n, nic.Name, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(nic);
            }
        }

        return list.ToArray();
    }
}
