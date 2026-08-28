using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Passless.Core.Abstractions;
using Passless.Core.Entities;

namespace Passless.Infrastructure.Tokens;

internal sealed class TokenService(
    PasslessDbContext db,
    AccessTokenIssuer accessTokens,
    IAuditLog audit,
    IOptions<TokenOptions> options,
    TimeProvider time) : ITokenService
{
    public IssuedTokens EnlistInitialTokens(Guid userId, Guid sessionId, Guid familyId)
    {
        var now = time.GetUtcNow();
        var settings = options.Value;

        var (id, token, hash) = RefreshTokenSecret.Create();
        var expiresAt = now + settings.RefreshTokenLifetime;

        db.RefreshTokens.Add(new RefreshToken(id, familyId, hash, now, expiresAt));

        audit.Enlist(new AuditEntry(
            AuditEventType.RefreshTokenIssued,
            AuditSeverity.Info,
            userId,
            Metadata: new Dictionary<string, string>
            {
                ["family"] = familyId.ToString(),
                ["session"] = sessionId.ToString(),
                // The token's id, never the token. Enough to follow a rotation
                // chain through the audit log; useless to anyone holding it.
                ["refresh_token"] = id.ToString(),
            }));

        var access = accessTokens.Issue(userId, sessionId, now);
        return new IssuedTokens(access.Token, access.ExpiresAt, token, expiresAt);
    }

    public async Task<RefreshResult> RefreshAsync(
        string presentedToken,
        CancellationToken cancellationToken = default)
    {
        if (!RefreshTokenSecret.TryParse(presentedToken, out var tokenId, out var secret))
        {
            return await RefuseAsync(RefreshFailure.UnknownToken, null, null, null, cancellationToken);
        }

        var now = time.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var stored = await db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tokenId, cancellationToken);

        if (stored is null)
        {
            return await RefuseAsync(RefreshFailure.UnknownToken, null, null, transaction, cancellationToken);
        }

        // The gate. Everything before this located a row by a non-secret id;
        // this is the only comparison that decides whether the caller holds the
        // token, and it runs in fixed time regardless of how many leading bytes
        // happen to match.
        if (!CryptographicOperations.FixedTimeEquals(stored.TokenHash, RefreshTokenSecret.Hash(secret)))
        {
            // A real token id with the wrong secret. Recorded, because it means
            // somebody has half of a credential — but deliberately *not* treated
            // as family compromise: if it were, guessing ids would be a way to
            // sign other people out.
            return await RefuseAsync(
                RefreshFailure.UnknownToken,
                stored.FamilyId,
                "secret_mismatch",
                transaction,
                cancellationToken);
        }

        // Serialises everything that touches this family: rotation, reuse
        // detection and session revocation all queue behind this one row.
        //
        // Without it, the compare-and-swap below still guarantees one winner per
        // token, but nothing stops a legitimate rotation from passing its
        // "family still valid" check moments before a concurrent reuse detection
        // invalidates that family — issuing a fresh token into a lineage that is
        // already dead. Family state and token state live in different rows, so
        // one row lock has to cover both.
        //
        // Always taken first and never in company, so there is no lock ordering
        // to get wrong. Granularity is one family, which is one session.
        var family = await db.TokenFamilies
            .FromSql($"SELECT * FROM token_families WHERE id = {stored.FamilyId} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

        if (family is null)
        {
            return await RefuseAsync(RefreshFailure.UnknownToken, null, null, transaction, cancellationToken);
        }

        if (family.IsInvalidated)
        {
            return await RefuseAsync(RefreshFailure.FamilyInvalidated, family.Id, null, transaction, cancellationToken);
        }

        var replacementId = Guid.NewGuid();

        // Compare and swap. The WHERE clause is the precondition, so there is no
        // interval between deciding the token is unconsumed and marking it
        // consumed -- they are one statement. A second caller blocks on the row
        // lock, re-evaluates against the committed row, matches nothing, and
        // updates zero rows. Rows affected is the verdict.
        var consumed = await db.RefreshTokens
            .Where(t => t.Id == tokenId && t.ConsumedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.ConsumedAt, now)
                    .SetProperty(t => t.ReplacedById, replacementId),
                cancellationToken);

        if (consumed == 0)
        {
            return await ClassifyLostRaceAsync(tokenId, family, now, transaction, cancellationToken);
        }

        var session = await db.Sessions
            .FirstOrDefaultAsync(s => s.FamilyId == family.Id, cancellationToken);

        if (session is null || session.IsRevoked)
        {
            // The family outlived its session. Nothing to refresh into.
            return await RefuseAsync(RefreshFailure.FamilyInvalidated, family.Id, null, transaction, cancellationToken);
        }

        var settings = options.Value;
        var (_, token, hash) = RefreshTokenSecret.CreateWithId(replacementId);
        var expiresAt = now + settings.RefreshTokenLifetime;

        db.RefreshTokens.Add(new RefreshToken(replacementId, family.Id, hash, now, expiresAt));
        session.Touch(now);

        audit.Enlist(new AuditEntry(
            AuditEventType.RefreshTokenRotated,
            AuditSeverity.Info,
            family.UserId,
            Metadata: new Dictionary<string, string>
            {
                ["family"] = family.Id.ToString(),
                ["consumed"] = tokenId.ToString(),
                ["issued"] = replacementId.ToString(),
            }));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var access = accessTokens.Issue(family.UserId, session.Id, now);
        return RefreshResult.Rotated(new IssuedTokens(access.Token, access.ExpiresAt, token, expiresAt));
    }

    /// <summary>
    /// The compare-and-swap matched nothing. Work out why.
    /// </summary>
    /// <remarks>
    /// This runs while the family lock is held, so no competing rotation can be
    /// in flight — a competitor has either committed, and its effect is visible
    /// here, or it is blocked behind us. The row read below is therefore the
    /// settled state and not a snapshot that might still change.
    /// </remarks>
    private async Task<RefreshResult> ClassifyLostRaceAsync(
        Guid tokenId,
        TokenFamily family,
        DateTimeOffset now,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var current = await db.RefreshTokens
            .AsNoTracking()
            .FirstAsync(t => t.Id == tokenId, cancellationToken);

        if (current.ConsumedAt is null)
        {
            return await RefuseAsync(RefreshFailure.Expired, family.Id, null, transaction, cancellationToken);
        }

        // A token that was already spent has come back.
        //
        // There is no way to tell the legitimate client replaying from the thief
        // using a stolen copy: both present the same bytes. Refusing only this
        // request leaves whichever of them holds the newer token still signed
        // in, and if that is the attacker, the theft succeeded. So the whole
        // lineage goes.
        //
        // The cost, stated plainly: a client that double-submits -- two tabs, or
        // a retry after a timeout -- does this to itself and is signed out
        // everywhere. The usual mitigation is a few seconds' grace in which the
        // immediately preceding token is accepted if it resolves to the same
        // replacement, which a thief is unlikely to be racing inside. Not built
        // here, because the strict rule is what this repository is demonstrating.
        family.Invalidate(TokenFamilyInvalidationReason.TokenReuseDetected, now);

        var revokedSessions = await db.Sessions
            .Where(s => s.FamilyId == family.Id && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, now), cancellationToken);

        var metadata = new Dictionary<string, string>
        {
            ["family"] = family.Id.ToString(),
            ["replayed"] = tokenId.ToString(),
            ["consumed_at"] = current.ConsumedAt.Value.ToString("O"),
            ["sessions_revoked"] = revokedSessions.ToString(),
        };

        audit.Enlist(new AuditEntry(
            AuditEventType.RefreshTokenReuseDetected,
            AuditSeverity.Critical,
            family.UserId,
            metadata));

        audit.Enlist(new AuditEntry(
            AuditEventType.TokenFamilyInvalidated,
            AuditSeverity.Critical,
            family.UserId,
            new Dictionary<string, string>
            {
                ["family"] = family.Id.ToString(),
                ["reason"] = TokenFamilyInvalidationReason.TokenReuseDetected.ToString(),
            }));

        if (revokedSessions > 0)
        {
            audit.Enlist(new AuditEntry(
                AuditEventType.SessionRevoked,
                AuditSeverity.Notice,
                family.UserId,
                new Dictionary<string, string>
                {
                    ["family"] = family.Id.ToString(),
                    ["count"] = revokedSessions.ToString(),
                    ["reason"] = "token_reuse_detected",
                }));
        }

        // Invalidation, revocation and all three audit rows commit together. A
        // detection that recorded the alarm but failed to revoke would be worse
        // than not detecting at all.
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return RefreshResult.Refused(RefreshFailure.ReuseDetected);
    }

    private async Task<RefreshResult> RefuseAsync(
        RefreshFailure failure,
        Guid? familyId,
        string? reasonOverride,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        // Roll back before recording, not after.
        //
        // Every refusal above happens with a transaction open -- often holding
        // the family lock. Writing the audit row into that transaction and then
        // letting it fall out of scope undisposed would roll the audit row back
        // along with everything else, so the rejections that matter most would
        // be the ones that left no trace. Ending the transaction first puts the
        // audit write on a fresh implicit one, and releases the family lock
        // while the log line is being written rather than after.
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        db.ChangeTracker.Clear();

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["reason"] = reasonOverride ?? failure.ToString(),
        };

        if (familyId is { } id)
        {
            metadata["family"] = id.ToString();
        }

        await audit.WriteAsync(
            new AuditEntry(AuditEventType.RefreshTokenRejected, AuditSeverity.Notice, null, metadata),
            cancellationToken);

        return RefreshResult.Refused(failure);
    }
}
