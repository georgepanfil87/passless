using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Passless.Core.Abstractions;
using Passless.Core.Entities;
using Passless.Infrastructure;
using Passless.Infrastructure.Tokens;

namespace Passless.Api.Features.Sessions;

/// <summary>Who asked for a revocation, and about what.</summary>
internal enum RevocationScope
{
    /// <summary>The session the request itself is authenticated with.</summary>
    Self,

    /// <summary>Another device belonging to the same account.</summary>
    OtherDevice,

    /// <summary>Part of a sign-out-everywhere-else sweep.</summary>
    AllOthers,
}

internal sealed class SessionService(
    PasslessDbContext db,
    IAuditLog audit,
    ISessionRevocationCache revocations,
    ILocationResolver locations,
    IOptions<TokenOptions> tokens,
    TimeProvider time)
{
    public async Task<IReadOnlyList<SessionView>> ListAsync(
        Guid userId,
        Guid currentSessionId,
        CancellationToken cancellationToken)
    {
        // Active only. This is the query the partial index on sessions exists
        // for, and a list of devices somebody signed out of months ago is not
        // what the screen is asking.
        var sessions = await db.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(cancellationToken);

        return sessions.Select(session =>
        {
            var location = locations.Resolve(session.Ip);

            return new SessionView(
                session.Id,
                session.DeviceLabel,
                session.UserAgent,
                location.City,
                location.Country,
                location.Describe(),
                session.CreatedAt,
                session.LastSeenAt,
                session.Id == currentSessionId);
        }).ToList();
    }

    public async Task<bool> RevokeAsync(
        Guid userId,
        Guid currentSessionId,
        Guid targetSessionId,
        CancellationToken cancellationToken)
    {
        // Scoped to the caller's own sessions in the query itself. A session
        // belonging to somebody else and a session that does not exist both come
        // back null here, so there is one code path and no chance of the two
        // diverging later into an oracle.
        var session = await db.Sessions
            .FirstOrDefaultAsync(
                s => s.Id == targetSessionId && s.UserId == userId && s.RevokedAt == null,
                cancellationToken);

        if (session is null)
        {
            return false;
        }

        var scope = session.Id == currentSessionId ? RevocationScope.Self : RevocationScope.OtherDevice;
        await RevokeOneAsync(session, scope, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> RevokeOthersAsync(
        Guid userId,
        Guid currentSessionId,
        CancellationToken cancellationToken)
    {
        var sessions = await db.Sessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != currentSessionId)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            await RevokeOneAsync(session, RevocationScope.AllOthers, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return sessions.Count;
    }

    /// <summary>
    /// Revoking a session and killing its refresh lineage are one act.
    /// </summary>
    /// <remarks>
    /// Three things have to happen together, and each closes a different door.
    /// The session row stops it appearing as a live device. The token family
    /// invalidation stops the refresh token it holds from minting anything new.
    /// The cache entry stops the access token it already has from being honoured
    /// before it expires. Doing any two of the three leaves the device signed in
    /// by some route.
    /// </remarks>
    private async Task RevokeOneAsync(
        Session session,
        RevocationScope scope,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        session.Revoke(now);

        var family = await db.TokenFamilies
            .FirstOrDefaultAsync(f => f.Id == session.FamilyId, cancellationToken);

        family?.Invalidate(TokenFamilyInvalidationReason.SessionRevoked, now);

        // The entry lives exactly as long as a token naming this session could
        // still be inside its validity window; after that it would be answering
        // a question nobody can ask.
        await revocations.RevokeAsync(session.Id, tokens.Value.AccessTokenLifetime, cancellationToken);

        audit.Enlist(new AuditEntry(
            AuditEventType.SessionRevoked,
            AuditSeverity.Notice,
            session.UserId,
            new Dictionary<string, string>
            {
                ["session"] = session.Id.ToString(),
                ["family"] = session.FamilyId.ToString(),
                ["scope"] = scope switch
                {
                    RevocationScope.Self => "self",
                    RevocationScope.OtherDevice => "other_device",
                    _ => "all_others",
                },
                ["device"] = session.DeviceLabel,
            }));
    }
}
