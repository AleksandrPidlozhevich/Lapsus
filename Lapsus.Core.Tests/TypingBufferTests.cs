using Lapsus.Core.Input;

namespace Lapsus.Core.Tests;

public class TypingBufferTests
{
    [Fact]
    public void Idle_segment_is_dropped()
    {
        var buffer = new TypingBuffer();
        foreach (var c in "secret")
            buffer.Append(c);

        Assert.True(buffer.DropIfIdle(TimeSpan.Zero));

        Assert.Equal(0, buffer.Length);
        Assert.Equal(string.Empty, buffer.Segment);
    }

    [Fact]
    public void Segment_survives_while_it_is_fresh()
    {
        var buffer = new TypingBuffer();
        foreach (var c in "ghbdtn")
            buffer.Append(c);

        Assert.False(buffer.DropIfIdle(TimeSpan.FromHours(1)));

        Assert.Equal("ghbdtn", buffer.Segment);
    }

    [Fact]
    public void Dropping_reports_whether_it_actually_dropped_anything()
    {
        var buffer = new TypingBuffer();
        Assert.False(buffer.DropIfIdle(TimeSpan.Zero));

        buffer.Append('a');
        Assert.True(buffer.DropIfIdle(TimeSpan.Zero));
        Assert.False(buffer.DropIfIdle(TimeSpan.Zero));
    }

    [Theory]
    [InlineData("pyf.", "pyf.")]
    [InlineData("hello ghbdtn?", "ghbdtn?")]
    [InlineData("co-op", "co-op")]
    [InlineData("word ", "")]
    [InlineData("", "")]
    public void Current_chunk_is_everything_since_the_last_whitespace(string typed, string expected)
    {
        var buffer = new TypingBuffer();
        foreach (var c in typed)
            buffer.Append(c);

        Assert.Equal(expected, buffer.CurrentChunk);
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("abc ", false)]
    [InlineData("", false)]
    [InlineData("hello,", true)]
    [InlineData("hf,", true)]
    [InlineData("hf,j", true)]
    public void Ends_inside_chunk_says_whether_the_next_key_would_continue_one(string typed, bool expected)
    {
        var buffer = new TypingBuffer();
        foreach (var c in typed)
            buffer.Append(c);

        Assert.Equal(expected, buffer.EndsInsideChunk);
    }

    [Fact]
    public void Segment_keeps_the_whole_line_including_spaces()
    {
        var buffer = new TypingBuffer();
        foreach (var c in "rfif c vjkjrjv")
            buffer.Append(c);

        Assert.Equal("rfif c vjkjrjv", buffer.Segment);
        Assert.Equal(14, buffer.Length);
    }

    [Fact]
    public void Trailing_space_stays_in_the_segment()
    {
        var buffer = new TypingBuffer();
        foreach (var c in "vjkjrjv ")
            buffer.Append(c);

        Assert.Equal("vjkjrjv ", buffer.Segment);
        Assert.Equal(string.Empty, buffer.CurrentChunk);
    }

    [Fact]
    public void Newline_starts_a_new_segment()
    {
        var buffer = new TypingBuffer();
        foreach (var c in "first\nsecond")
            buffer.Append(c);

        Assert.Equal("second", buffer.Segment);
    }

    [Fact]
    public void Backspace_removes_last_character()
    {
        var buffer = new TypingBuffer();
        foreach (var c in "carr")
            buffer.Append(c);
        buffer.Backspace();

        Assert.Equal("car", buffer.Segment);
    }

    [Fact]
    public void Replace_trailing_text_keeps_the_buffer_in_sync_after_auto_correction()
    {
        var buffer = new TypingBuffer();
        foreach (var c in "before ghbdtn ")
            buffer.Append(c);

        var replaced = buffer.TryReplaceTrailing("ghbdtn ", "привет ");

        Assert.True(replaced);
        Assert.Equal("before привет ", buffer.Segment);
    }

    [Fact]
    public void Replace_trailing_text_rejects_a_stale_auto_correction()
    {
        var buffer = new TypingBuffer();
        foreach (var c in "ghbdtn next")
            buffer.Append(c);

        var replaced = buffer.TryReplaceTrailing("ghbdtn ", "привет ");

        Assert.False(replaced);
        Assert.Equal("ghbdtn next", buffer.Segment);
    }

    [Fact]
    public void Reset_clears_the_segment()
    {
        var buffer = new TypingBuffer();
        buffer.Append('x');
        buffer.Reset();

        Assert.Equal(string.Empty, buffer.Segment);
    }
}
