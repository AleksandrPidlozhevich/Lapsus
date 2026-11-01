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

// An expired or refunded subscription is not reported at all. IsTrial: the yearly subscription is still
// in its free introductory period.
public readonly record struct StoreEntitlement(StorePlan Plan, DateTimeOffset? Expires = null, bool IsTrial = false)
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
    // The trial is the yearly subscription's free introductory offer, so the App Store counts it.
    public static StoreAccess Decide(bool isStoreBuild, StoreEntitlement entitlement, DateTimeOffset now)
    {
        if (!isStoreBuild)
            return new StoreAccess(StoreAccessKind.NotStoreBuild);

        if (entitlement.Plan == StorePlan.Lifetime)
            return new StoreAccess(StoreAccessKind.Lifetime);

        // Entitlements are re-read now and then; one that ran out in between no longer counts.
        if (entitlement.Plan != StorePlan.Yearly || entitlement.Expires <= now)
            return new StoreAccess(StoreAccessKind.Free);

        return entitlement.IsTrial
            ? new StoreAccess(StoreAccessKind.Trial, TrialDaysLeft(entitlement.Expires, now), entitlement.Expires)
            : new StoreAccess(StoreAccessKind.Subscription, Renews: entitlement.Expires);
    }

    // The last day of the trial still counts as one day left.
    public static int TrialDaysLeft(DateTimeOffset? ends, DateTimeOffset now) =>
        ends is { } end ? Math.Max(1, (int)Math.Ceiling((end - now).TotalDays)) : 0;

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
