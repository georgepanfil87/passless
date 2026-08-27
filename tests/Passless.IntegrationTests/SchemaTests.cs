using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Passless.Core.Entities;
using Passless.Infrastructure;

namespace Passless.IntegrationTests;

[Collection(PasslessCollection.Name)]
public sealed class SchemaTests(PasslessFixture fixture)
{
    [Fact]
    public async Task Schema_applies_cleanly_to_an_empty_database()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        var pending = await db.Database.GetPendingMigrationsAsync();

        Assert.NotEmpty(applied);
        Assert.Empty(pending);

        // Migrations reporting success is not the same as the schema existing:
        // assert the tables are actually there.
        var tables = await QueryStringsAsync(
            """
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            """);

        Assert.Contains("users", tables);
        Assert.Contains("credentials", tables);
        Assert.Contains("token_families", tables);
        Assert.Contains("refresh_tokens", tables);
        Assert.Contains("sessions", tables);
        Assert.Contains("audit_events", tables);
    }

    [Fact]
    public async Task Credential_id_is_unique_across_the_whole_table()
    {
        var sharedCredentialId = RandomBytes(32);

        var first = await NewUserAsync();
        var second = await NewUserAsync();

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
            db.Credentials.Add(NewCredential(first, sharedCredentialId));
            await db.SaveChangesAsync();
        }

        await using var conflicting = fixture.CreateScope();
        var conflictDb = conflicting.ServiceProvider.GetRequiredService<PasslessDbContext>();

        // Different user, same credential. If this were permitted an assertion
        // could resolve to whichever account the lookup returned first.
        conflictDb.Credentials.Add(NewCredential(second, sharedCredentialId));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => conflictDb.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
    }

    [Fact]
    public async Task Audit_events_cannot_be_updated_through_the_change_tracker()
    {
        var recorded = await NewAuditEventAsync();

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var loaded = await db.AuditEvents.SingleAsync(e => e.Id == recorded);

        // The entity exposes no mutator at all, so reaching the modified state
        // means going around it. The guard still refuses.
        db.Entry(loaded).State = EntityState.Modified;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Audit_events_cannot_be_deleted_through_the_change_tracker()
    {
        var recorded = await NewAuditEventAsync();

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var loaded = await db.AuditEvents.SingleAsync(e => e.Id == recorded);
        db.AuditEvents.Remove(loaded);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("UPDATE audit_events SET severity = 'Info' WHERE id = @id")]
    [InlineData("DELETE FROM audit_events WHERE id = @id")]
    [InlineData("TRUNCATE audit_events CASCADE")]
    public async Task Audit_events_are_append_only_in_the_database(string statement)
    {
        var recorded = await NewAuditEventAsync();

        // Raw SQL on a fresh connection: this bypasses the entity, the change
        // tracker and the SaveChanges guard entirely. Whatever is left standing
        // here is the guarantee the data actually has.
        await using var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(statement, connection);
        if (statement.Contains("@id", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("id", recorded);
        }

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.Contains("append-only", error.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_a_user_that_has_audit_events_is_refused()
    {
        var userId = await NewUserAsync();
        await NewAuditEventAsync(userId);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();

        var user = await db.Users.SingleAsync(u => u.Id == userId);
        db.Users.Remove(user);

        // ON DELETE RESTRICT, deliberately. SET NULL would be an UPDATE against
        // audit_events, which the trigger rejects -- so the two rules would
        // deadlock at the moment someone tried to erase an account. Accounts are
        // disabled instead, and this is what makes that the only path.
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);
    }

    [Fact]
    public async Task Refresh_tokens_round_trip_as_a_hash_and_nothing_else()
    {
        var userId = await NewUserAsync();
        var familyId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var hash = RandomBytes(RefreshToken.TokenHashLength);
        var now = DateTimeOffset.UtcNow;

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
            db.TokenFamilies.Add(new TokenFamily(familyId, userId, now));
            db.RefreshTokens.Add(new RefreshToken(tokenId, familyId, hash, now, now.AddDays(30)));
            await db.SaveChangesAsync();
        }

        // The stored column set is the assertion: if a plaintext column ever
        // appeared, it would show up here.
        var columns = await QueryStringsAsync(
            """
            SELECT column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'refresh_tokens'
            """);

        Assert.Contains("token_hash", columns);
        Assert.DoesNotContain(columns, c => c is "token" or "token_value" or "plaintext" or "secret");

        await using var verify = fixture.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PasslessDbContext>();
        var stored = await verifyDb.RefreshTokens.SingleAsync(t => t.Id == tokenId);
        Assert.Equal(hash, stored.TokenHash);
    }

    [Fact]
    public void Refresh_token_refuses_anything_that_is_not_a_sha256_digest()
    {
        var now = DateTimeOffset.UtcNow;

        // The shape of the mistake this guards against: passing the token itself
        // where a digest belongs. A 64-byte token would sail through a length
        // check that only asserted "not empty".
        var error = Assert.Throws<ArgumentException>(() => new RefreshToken(
            Guid.NewGuid(), Guid.NewGuid(), RandomBytes(64), now, now.AddDays(30)));

        Assert.Contains("digest", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> NewUserAsync()
    {
        var id = Guid.NewGuid();
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        db.Users.Add(new User(id, $"user-{id:N}@example.test", "Test User", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> NewAuditEventAsync(Guid? userId = null)
    {
        var id = Guid.NewGuid();
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PasslessDbContext>();
        db.AuditEvents.Add(new AuditEvent(
            id,
            userId,
            AuditEventType.RefreshTokenReuseDetected,
            AuditSeverity.Critical,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["family"] = Guid.NewGuid().ToString() }));
        await db.SaveChangesAsync();
        return id;
    }

    private static Credential NewCredential(Guid userId, byte[] credentialId) => new(
        Guid.NewGuid(),
        userId,
        credentialId,
        RandomBytes(64),
        signatureCounter: 0,
        aaguid: Guid.Empty,
        transports: ["internal", "hybrid"],
        backupEligible: true,
        backupState: true,
        friendlyName: "Test authenticator",
        createdAt: DateTimeOffset.UtcNow);

    private static byte[] RandomBytes(int count) =>
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(count);

    private async Task<List<string>> QueryStringsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
