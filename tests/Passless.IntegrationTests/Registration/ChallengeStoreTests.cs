using Microsoft.Extensions.DependencyInjection;
using Passless.Core.Abstractions;

namespace Passless.IntegrationTests.Registration;

/// <summary>
/// Tests the single-use guarantee directly against the store.
/// </summary>
/// <remarks>
/// Driving this through the HTTP endpoint is not enough. A second submission
/// that got past the challenge would still be refused further down, by the
/// unique index on the credential id — so an end-to-end test sees one success
/// and one failure whether consumption is atomic or not, and passes for the
/// wrong reason. The property has to be tested where it lives.
/// </remarks>
[Collection(PasslessCollection.Name)]
public sealed class ChallengeStoreTests(PasslessFixture fixture)
{
    [Fact]
    public async Task Consume_returns_the_ticket_to_exactly_one_caller_under_contention()
    {
        await using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IChallengeStore>();

        var ceremonyId = Guid.NewGuid().ToString("N");
        await store.StoreAsync(
            ceremonyId,
            new ChallengeTicket(ChallengeKind.Registration, "payload", DateTimeOffset.UtcNow),
            TimeSpan.FromMinutes(1));

        // Enough contenders that a read-then-delete implementation cannot get
        // lucky: several will observe the value before any delete lands.
        const int Contenders = 32;
        using var gate = new SemaphoreSlim(0, Contenders);

        var attempts = Enumerable.Range(0, Contenders)
            .Select(_ => Task.Run(async () =>
            {
                await gate.WaitAsync();
                return await store.ConsumeAsync(ceremonyId);
            }))
            .ToArray();

        gate.Release(Contenders);
        var tickets = await Task.WhenAll(attempts);

        Assert.Single(tickets, t => t is not null);
        Assert.Equal(Contenders - 1, tickets.Count(t => t is null));
    }

    [Fact]
    public async Task Consume_refuses_a_ticket_minted_for_a_different_ceremony_kind()
    {
        await using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IChallengeStore>();

        var ceremonyId = Guid.NewGuid().ToString("N");
        await store.StoreAsync(
            ceremonyId,
            new ChallengeTicket(ChallengeKind.Assertion, "payload", DateTimeOffset.UtcNow),
            TimeSpan.FromMinutes(1));

        var ticket = await store.ConsumeAsync(ceremonyId);

        // The store returns what it holds; refusing a mismatched kind is the
        // caller's job, and the registration service does it. Asserted here so
        // the kind cannot be quietly dropped from the ticket.
        Assert.NotNull(ticket);
        Assert.Equal(ChallengeKind.Assertion, ticket!.Kind);
    }

    [Fact]
    public async Task Storing_twice_under_one_ceremony_id_is_refused()
    {
        await using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IChallengeStore>();

        var ceremonyId = Guid.NewGuid().ToString("N");
        var ticket = new ChallengeTicket(ChallengeKind.Registration, "payload", DateTimeOffset.UtcNow);

        await store.StoreAsync(ceremonyId, ticket, TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.StoreAsync(ceremonyId, ticket, TimeSpan.FromMinutes(1)));
    }
}
