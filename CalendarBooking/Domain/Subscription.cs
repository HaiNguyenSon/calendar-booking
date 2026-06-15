namespace CalendarBooking.Domain;

/// <summary>
/// One user subscribing to another (a directed follow). The join table that lets a user have
/// many subscriptions and a user have many subscribers. Unique per (subscriber, target).
/// </summary>
public class Subscription
{
    public Guid Id { get; set; }

    /// <summary>The follower.</summary>
    public string SubscriberId { get; set; } = string.Empty;
    public ApplicationUser? Subscriber { get; set; }

    /// <summary>The user being subscribed to.</summary>
    public string TargetId { get; set; } = string.Empty;
    public ApplicationUser? Target { get; set; }

    public DateTime CreatedUtc { get; set; }
}
