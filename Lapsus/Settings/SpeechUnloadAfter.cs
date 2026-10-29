using System;

namespace Lapsus.Settings;

// How long the speech model stays in memory after the last dictation (Handy's choice of steps).
public enum SpeechUnloadAfter
{
    Immediately,
    Min2,
    Min5,
    Min15,
    Hour1,
    Never
}

public static class SpeechUnloadAfterExtensions
{
    public static TimeSpan? Delay(this SpeechUnloadAfter value) => value switch
    {
        SpeechUnloadAfter.Immediately => TimeSpan.Zero,
        SpeechUnloadAfter.Min2 => TimeSpan.FromMinutes(2),
        SpeechUnloadAfter.Min5 => TimeSpan.FromMinutes(5),
        SpeechUnloadAfter.Min15 => TimeSpan.FromMinutes(15),
        SpeechUnloadAfter.Hour1 => TimeSpan.FromHours(1),
        _ => null
    };
}
