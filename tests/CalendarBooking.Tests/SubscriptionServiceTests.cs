using CalendarBooking.Services;
using Microsoft.EntityFrameworkCore;

namespace CalendarBooking.Tests;

public class SubscriptionServiceTests
{
    private static readonly DateTime Now = TestDb.Now;

    [Fact]
    public async Task Subscribe_by_public_code_creates_the_subscription()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var svc = new SubscriptionService(db);

        var result = await svc.SubscribeAsync(alice.Id, bob.PublicId, Now);

        Assert.True(result.Ok);
        Assert.True(await svc.IsSubscribedAsync(alice.Id, bob.Id));
        Assert.Equal(1, await svc.GetSubscriberCountAsync(bob.Id));
    }

    [Fact]
    public async Task Subscribe_is_case_insensitive_on_the_code()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var svc = new SubscriptionService(db);

        var result = await svc.SubscribeAsync(alice.Id, $"  {bob.PublicId.ToLowerInvariant()} ", Now);

        Assert.True(result.Ok);
        Assert.True(await svc.IsSubscribedAsync(alice.Id, bob.Id));
    }

    [Fact]
    public async Task Subscribe_to_an_unknown_code_fails()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = new SubscriptionService(db);

        var result = await svc.SubscribeAsync(alice.Id, "ZZZZZZZZZZ", Now);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Subscribe_to_yourself_fails()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var svc = new SubscriptionService(db);

        var result = await svc.SubscribeAsync(alice.Id, alice.PublicId, Now);

        Assert.False(result.Ok);
        Assert.Equal(0, await db.Subscriptions.CountAsync());
    }

    [Fact]
    public async Task Subscribe_twice_is_idempotent()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var svc = new SubscriptionService(db);

        Assert.True((await svc.SubscribeAsync(alice.Id, bob.PublicId, Now)).Ok);
        Assert.True((await svc.SubscribeAsync(alice.Id, bob.PublicId, Now)).Ok);

        Assert.Equal(1, await db.Subscriptions.CountAsync());
    }

    [Fact]
    public async Task Unsubscribe_removes_the_subscription()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var svc = new SubscriptionService(db);
        await svc.SubscribeAsync(alice.Id, bob.PublicId, Now);

        var result = await svc.UnsubscribeAsync(alice.Id, bob.Id);

        Assert.True(result.Ok);
        Assert.False(await svc.IsSubscribedAsync(alice.Id, bob.Id));
    }

    [Fact]
    public async Task GetSubscriptions_lists_who_you_follow()
    {
        using var db = TestDb.NewContext();
        var alice = TestDb.AddUser(db, "alice");
        var bob = TestDb.AddUser(db, "bob");
        var carol = TestDb.AddUser(db, "carol");
        var svc = new SubscriptionService(db);
        await svc.SubscribeAsync(alice.Id, bob.PublicId, Now);
        await svc.SubscribeAsync(alice.Id, carol.PublicId, Now);

        var subscriptions = await svc.GetSubscriptionsAsync(alice.Id);

        Assert.Equal(2, subscriptions.Count);
        Assert.Contains(subscriptions, s => s.Nickname == "bob");
        Assert.Contains(subscriptions, s => s.Nickname == "carol");
    }
}
