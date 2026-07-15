using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioHQ.App;

public sealed record AudioEndpointIdentity(string Id, string Name);

/// <summary>Resolves a persisted audio endpoint after Windows replaces its volatile endpoint id.</summary>
public static class AudioEndpointIdentityResolver
{
    public static string? Resolve(
        string savedId,
        string savedName,
        IEnumerable<AudioEndpointIdentity> activeEndpoints,
        ISet<string>? reservedIds = null)
    {
        var endpoints = activeEndpoints.ToList();
        var exact = endpoints.FirstOrDefault(endpoint =>
            endpoint.Id == savedId && reservedIds?.Contains(endpoint.Id) != true);
        if (exact is not null) return exact.Id;

        if (string.IsNullOrWhiteSpace(savedName)) return null;

        var matches = endpoints
            .Where(endpoint => reservedIds?.Contains(endpoint.Id) != true)
            .Where(endpoint => string.Equals(endpoint.Name, savedName, StringComparison.OrdinalIgnoreCase))
            .Select(endpoint => endpoint.Id)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }
}
