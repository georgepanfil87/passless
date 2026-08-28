using System.Net;

namespace Passless.Core.Abstractions;

/// <param name="Country">
/// A country name, or the marker <see cref="PrivateNetwork"/> uses. Never
/// coordinates: this type has no room for a latitude by design.
/// </param>
public sealed record CoarseLocation(string? City, string? Country)
{
    public static readonly CoarseLocation Unknown = new(null, null);

    /// <summary>A marker rather than a geography — loopback and RFC 1918 space has no location.</summary>
    public static readonly CoarseLocation PrivateNetwork = new(null, "Local network");

    public string Describe() => (City, Country) switch
    {
        (null or "", null or "") => "Unknown location",
        (null or "", var country) => country!,
        (var city, null or "") => city!,
        var (city, country) => $"{city}, {country}",
    };
}

/// <summary>
/// Resolves an address to a coarse location, at read time.
/// </summary>
/// <remarks>
/// Nothing derived here is persisted. Storing a resolved location would add a
/// second piece of location data to every session row, on top of the address it
/// came from, and freeze it at the moment the session was created. Resolving on
/// the way out keeps the stored footprint to what step 3 already committed to.
/// </remarks>
public interface ILocationResolver
{
    CoarseLocation Resolve(IPAddress? address);
}
