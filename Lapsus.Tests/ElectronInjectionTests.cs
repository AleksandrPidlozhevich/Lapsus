using Lapsus.Input;

namespace Lapsus.Tests;

public class ElectronInjectionTests
{
    [Fact]
    public void Injected_key_times_stay_after_the_previous_event_when_the_clock_is_still()
    {
        ulong last = 5_000_000_000;
        var clock = last + 100;

        var first = InjectedEventTime.Next(clock, ref last);
        var second = InjectedEventTime.Next(clock, ref last);

        Assert.Equal(5_000_000_000UL + InjectedEventTime.StepNanoseconds, first);
        Assert.Equal(first + InjectedEventTime.StepNanoseconds, second);
    }

    [Fact]
    public void A_later_clock_reading_is_not_pulled_backwards()
    {
        ulong last = 1_000;
        var stamp = InjectedEventTime.Next(50_000_000, ref last);

        Assert.Equal(50_000_000UL, stamp);
        Assert.Equal(stamp, last);
    }

    [Theory]
    [InlineData("AXWebArea", "AXTextField")]
    [InlineData("AXWebArea", "AXGroup")]
    [InlineData("AXTextArea", "AXGroup")]
    [InlineData("AXScrollArea", "AXGroup")]
    public void The_document_is_visited_before_chrome_around_it(string earlier, string later)
    {
        Assert.True(CaretSearchOrder.Rank(earlier) < CaretSearchOrder.Rank(later));
    }

    [Fact]
    public void Buttons_are_not_part_of_the_caret_walk()
    {
        Assert.False(CaretSearchOrder.IsText("AXButton"));
        Assert.False(CaretSearchOrder.IsContainer("AXButton"));
        Assert.True(CaretSearchOrder.IsContainer("AXWebArea"));
        Assert.True(CaretSearchOrder.IsText("AXWebArea"));
    }

    [Fact]
    public void A_helper_process_belongs_to_the_same_app_as_its_bundle()
    {
        const string main = "/Applications/Cursor.app/Contents/MacOS/Cursor";
        const string helper =
            "/Applications/Cursor.app/Contents/Frameworks/Cursor Helper (Renderer).app/Contents/MacOS/Cursor Helper (Renderer)";

        Assert.True(MacAppIdentity.SameBundle(main, helper));
        Assert.False(MacAppIdentity.SameBundle(main, "/Applications/Notion.app/Contents/MacOS/Notion"));
    }

    [Fact]
    public void Safari_web_content_is_not_inside_the_Safari_bundle()
    {
        const string safari = "/Applications/Safari.app/Contents/MacOS/Safari";
        const string page =
            "/System/Library/Frameworks/WebKit.framework/Versions/A/XPCServices/com.apple.WebKit.WebContent.xpc/Contents/MacOS/com.apple.WebKit.WebContent";

        Assert.True(MacAppIdentity.IsWebContent(page));
        Assert.True(MacAppIdentity.IsWebKitHost(safari));
        Assert.False(MacAppIdentity.SameBundle(safari, page));
        Assert.False(MacAppIdentity.IsWebContent(safari));
    }
}
