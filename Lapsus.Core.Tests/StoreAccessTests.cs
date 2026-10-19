using Lapsus.Core.Licensing;

namespace Lapsus.Core.Tests;

public class StoreAccessTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    [Fact]
    public void The_website_build_gates_nothing_whatever_it_owns()
    {
        var access = StoreAccessPolicy.Decide(isStoreBuild: false, StoreEntitlement.None, trialStarted: Today.AddDays(-400), Today);

        Assert.Equal(StoreAccessKind.NotStoreBuild, access.Kind);
        Assert.True(access.PaidFeatures);
    }

    [Fact]
    public void A_first_launch_starts_the_full_trial()
    {
        var access = StoreAccessPolicy.Decide(true, StoreEntitlement.None, trialStarted: null, Today);

        Assert.Equal(StoreAccessKind.Trial, access.Kind);
        Assert.Equal(StoreAccessPolicy.TrialDays, access.TrialDaysLeft);
        Assert.True(access.PaidFeatures);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1, 59)]
    [InlineData(59, 1)]
    public void The_trial_counts_down_by_calendar_day(int daysAgo, int left)
    {
        var access = StoreAccessPolicy.Decide(true, StoreEntitlement.None, Today.AddDays(-daysAgo), Today);

        Assert.Equal(StoreAccessKind.Trial, access.Kind);
        Assert.Equal(left, access.TrialDaysLeft);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(365)]
    public void After_the_trial_only_the_free_part_is_left(int daysAgo)
    {
        var access = StoreAccessPolicy.Decide(true, StoreEntitlement.None, Today.AddDays(-daysAgo), Today);

        Assert.Equal(StoreAccessKind.Free, access.Kind);
        Assert.False(access.PaidFeatures);
    }

    [Fact]
    public void Moving_the_clock_back_never_lengthens_the_trial()
    {
        Assert.Equal(StoreAccessPolicy.TrialDays, StoreAccessPolicy.TrialDaysLeft(Today.AddDays(30), Today));
    }

    [Fact]
    public void A_subscription_unlocks_and_reports_its_renewal()
    {
        var renews = new DateTimeOffset(2027, 10, 16, 0, 0, 0, TimeSpan.Zero);

        var access = StoreAccessPolicy.Decide(true, new StoreEntitlement(StorePlan.Yearly, renews), Today.AddDays(-400), Today);

        Assert.Equal(StoreAccessKind.Subscription, access.Kind);
        Assert.Equal(renews, access.Renews);
        Assert.True(access.PaidFeatures);
    }

    [Fact]
    public void A_lifetime_purchase_unlocks_for_good()
    {
        var access = StoreAccessPolicy.Decide(true, new StoreEntitlement(StorePlan.Lifetime), Today.AddDays(-400), Today);

        Assert.Equal(StoreAccessKind.Lifetime, access.Kind);
        Assert.True(access.PaidFeatures);
    }

    [Fact]
    public void Lifetime_wins_over_a_subscription()
    {
        var owned = new[]
        {
            new StoreEntitlement(StorePlan.Yearly, DateTimeOffset.UtcNow.AddDays(100)),
            new StoreEntitlement(StorePlan.Lifetime)
        };

        Assert.Equal(StorePlan.Lifetime, StoreAccessPolicy.Strongest(owned).Plan);
    }

    [Fact]
    public void Of_two_subscriptions_the_later_end_wins()
    {
        var later = DateTimeOffset.UtcNow.AddDays(200);
        var owned = new[]
        {
            new StoreEntitlement(StorePlan.Yearly, DateTimeOffset.UtcNow.AddDays(10)),
            new StoreEntitlement(StorePlan.Yearly, later)
        };

        Assert.Equal(later, StoreAccessPolicy.Strongest(owned).Expires);
    }

    [Fact]
    public void Owning_nothing_is_none()
    {
        Assert.Equal(StorePlan.None, StoreAccessPolicy.Strongest([]).Plan);
    }

    [Theory]
    [InlineData(StoreProducts.Yearly, StorePlan.Yearly)]
    [InlineData(StoreProducts.Lifetime, StorePlan.Lifetime)]
    [InlineData("com.example.other", StorePlan.None)]
    public void Product_ids_map_to_plans(string productId, StorePlan plan)
    {
        Assert.Equal(plan, StoreAccessPolicy.PlanOf(productId));
    }
}
