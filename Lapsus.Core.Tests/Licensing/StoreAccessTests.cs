using Lapsus.Core.Licensing;

namespace Lapsus.Core.Tests.Licensing;

public class StoreAccessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_website_build_gates_nothing_whatever_it_owns()
    {
        var access = StoreAccessPolicy.Decide(isStoreBuild: false, StoreEntitlement.None, Now);

        Assert.Equal(StoreAccessKind.NotStoreBuild, access.Kind);
        Assert.True(access.PaidFeatures);
    }

    [Fact]
    public void Owning_nothing_is_the_free_part_only()
    {
        var access = StoreAccessPolicy.Decide(true, StoreEntitlement.None, Now);

        Assert.Equal(StoreAccessKind.Free, access.Kind);
        Assert.False(access.PaidFeatures);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(1.5, 2)]
    [InlineData(0.1, 1)]
    public void The_app_store_trial_unlocks_and_counts_down(double daysToEnd, int left)
    {
        var ends = Now.AddDays(daysToEnd);

        var access = StoreAccessPolicy.Decide(true, new StoreEntitlement(StorePlan.Yearly, ends, IsTrial: true), Now);

        Assert.Equal(StoreAccessKind.Trial, access.Kind);
        Assert.Equal(left, access.TrialDaysLeft);
        Assert.Equal(ends, access.Renews);
        Assert.True(access.PaidFeatures);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_subscription_that_ran_out_since_the_last_check_unlocks_nothing(bool isTrial)
    {
        var access = StoreAccessPolicy.Decide(true, new StoreEntitlement(StorePlan.Yearly, Now.AddMinutes(-1), isTrial), Now);

        Assert.Equal(StoreAccessKind.Free, access.Kind);
    }

    [Fact]
    public void A_subscription_unlocks_and_reports_its_renewal()
    {
        var renews = new DateTimeOffset(2027, 10, 16, 0, 0, 0, TimeSpan.Zero);

        var access = StoreAccessPolicy.Decide(true, new StoreEntitlement(StorePlan.Yearly, renews), Now);

        Assert.Equal(StoreAccessKind.Subscription, access.Kind);
        Assert.Equal(renews, access.Renews);
        Assert.True(access.PaidFeatures);
    }

    [Fact]
    public void A_lifetime_purchase_unlocks_for_good()
    {
        var access = StoreAccessPolicy.Decide(true, new StoreEntitlement(StorePlan.Lifetime), Now);

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
