using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace MediaDock.Api.Authentication;

public static class TrustedForwardedHeaders
{
    public const string TrustedNetworkConfigurationKey = "ForwardedHeaders:TrustedNetwork";

    public static ForwardedHeadersOptions? CreateOptions(IConfiguration configuration)
    {
        var networkValue = configuration[TrustedNetworkConfigurationKey];
        if (string.IsNullOrWhiteSpace(networkValue))
        {
            return null;
        }

        if (!System.Net.IPNetwork.TryParse(networkValue, out var trustedNetwork))
        {
            throw new InvalidOperationException(
                $"{TrustedNetworkConfigurationKey} must be a valid IP network in CIDR notation.");
        }

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Add(trustedNetwork);
        return options;
    }
}