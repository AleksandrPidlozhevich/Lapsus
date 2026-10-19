namespace Lapsus.Core.Licensing;

// The Mac App Store edition is the one place where something is paid for: neural correction and
// features added after it. Everything else stays free there too, and the website build gates
// nothing at all. These are the App Store products that pay for it.
public static class StoreProducts
{
    public const string Yearly = "com.lapsus.app.plus.yearly";

    public const string Lifetime = "com.lapsus.app.plus.lifetime";

    public static readonly string[] All = [Yearly, Lifetime];
}

public enum StorePlan
{
    None,
    Yearly,
    Lifetime
}

// What the App Store says the user owns right now. An expired or refunded subscription is not
// reported at all, so Yearly always means active.
public readonly record struct StoreEntitlement(StorePlan Plan, DateTimeOffset? Expires = null)
{
    public static StoreEntitlement None { get; } = new(StorePlan.None);
}

public enum StoreAccessKind
{
    // Not the App Store edition: nothing is gated.
    NotStoreBuild,
    Lifetime,
    Subscription,
    Trial,
    Free
}

public readonly record struct StoreAccess(StoreAccessKind Kind, int TrialDaysLeft = 0, DateTimeOffset? Renews = null)
{
    public bool PaidFeatures => Kind is not StoreAccessKind.Free;
}

public static class StoreAccessPolicy
{
    // Counted by the app from the first launch, so trying it needs no card and no subscription.
    public const int TrialDays = 60;

    public static StoreAccess Decide(bool isStoreBuild, StoreEntitlement entitlement, DateOnly? trialStarted, DateOnly today)
    {
        if (!isStoreBuild)
            return new StoreAccess(StoreAccessKind.NotStoreBuild);

        switch (entitlement.Plan)
        {
            case StorePlan.Lifetime:
                return new StoreAccess(StoreAccessKind.Lifetime);
            case StorePlan.Yearly:
                return new StoreAccess(StoreAccessKind.Subscription, Renews: entitlement.Expires);
        }

        var left = TrialDaysLeft(trialStarted, today);
        return left > 0
            ? new StoreAccess(StoreAccessKind.Trial, TrialDaysLeft: left)
            : new StoreAccess(StoreAccessKind.Free);
    }

    // No start date yet means the trial starts today. A start in the future (the clock was moved
    // back) never makes the trial longer than its full length.
    public static int TrialDaysLeft(DateOnly? trialStarted, DateOnly today)
    {
        var start = trialStarted ?? today;
        var elapsed = Math.Max(0, today.DayNumber - start.DayNumber);
        return Math.Clamp(TrialDays - elapsed, 0, TrialDays);
    }

    public static StorePlan PlanOf(string productId) => productId switch
    {
        StoreProducts.Lifetime => StorePlan.Lifetime,
        StoreProducts.Yearly => StorePlan.Yearly,
        _ => StorePlan.None
    };

    // Lifetime wins over a subscription bought before it; a subscription with the later end wins over another.
    public static StoreEntitlement Strongest(IEnumerable<StoreEntitlement> owned)
    {
        var best = StoreEntitlement.None;
        foreach (var item in owned)
        {
            if (item.Plan == StorePlan.Lifetime)
                return item;

            if (item.Plan == StorePlan.Yearly
                && (best.Plan == StorePlan.None || (item.Expires ?? DateTimeOffset.MinValue) > (best.Expires ?? DateTimeOffset.MinValue)))
                best = item;
        }

        return best;
    }
}
