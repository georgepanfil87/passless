using System.Net;
using System.Net.Sockets;
using Passless.Core.Abstractions;

namespace Passless.Infrastructure.Sessions;

/// <summary>
/// The resolver that ships: it recognises addresses that cannot have a location
/// and declines to guess about the rest.
/// </summary>
/// <remarks>
/// City-level lookup needs a GeoIP database — a data file, a licence, and a job
/// to keep it current — which is a large amount of machinery to bundle for a
/// reference implementation. What matters for the design is that the seam
/// exists and that the type on the other side of it cannot carry coordinates.
/// A deployment that wants real city data registers a MaxMind-backed
/// <see cref="ILocationResolver"/> in its place; nothing else changes.
/// </remarks>
public sealed class DefaultLocationResolver : ILocationResolver
{
    public CoarseLocation Resolve(IPAddress? address)
    {
        if (address is null)
        {
            return CoarseLocation.Unknown;
        }

        return IsPrivate(address) ? CoarseLocation.PrivateNetwork : CoarseLocation.Unknown;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var octets = address.GetAddressBytes();

        return octets[0] switch
        {
            10 => true,
            172 => octets[1] >= 16 && octets[1] <= 31,
            192 => octets[1] == 168,
            169 => octets[1] == 254,
            _ => false,
        };
    }
}
