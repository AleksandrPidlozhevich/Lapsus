using System;
using System.Collections.Generic;
using System.Linq;

namespace Lapsus.Speech;

// One downloadable file, pinned to a commit and its SHA-256 (from the Hugging Face listing at that commit),
// so what reaches the disk is exactly what was measured.
public sealed record SpeechFile(string Repo, string Revision, string Path, long Size, string Sha256);

public sealed record SpeechModel(string Id, string DisplayName, SpeechFile File, int RamMb, string License)
{
    public int SizeMb => (int)(File.Size / (1024 * 1024));
}

// Whisper models for dictation (ggml, whisper.cpp's own builds) and the Silero VAD every one of them needs.
// Stage 1 measured large-v3-turbo: q8_0 matches q5_0 for quality and is faster on a CPU, so it is the default.
public static class SpeechModelCatalog
{
    private const string WhisperRepo = "ggerganov/whisper.cpp";
    private const string WhisperRevision = "5359861c739e955e79d9a303bcbc70fb988958b1";

    public const string DefaultId = "whisper-large-v3-turbo-q8";

    public static readonly SpeechFile Vad = new(
        "ggml-org/whisper-vad", "9ffd54a1e1ee413ddf265af9913beaf518d1639b", "ggml-silero-v6.2.0.bin",
        885098, "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987");

    public static readonly IReadOnlyList<SpeechModel> Available =
    [
        new(DefaultId, "Whisper large-v3-turbo",
            new SpeechFile(WhisperRepo, WhisperRevision, "ggml-large-v3-turbo-q8_0.bin", 874188075,
                "317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1"),
            RamMb: 1100, License: "MIT"),
        new("whisper-large-v3-turbo-q5", "Whisper large-v3-turbo q5",
            new SpeechFile(WhisperRepo, WhisperRevision, "ggml-large-v3-turbo-q5_0.bin", 574041195,
                "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2"),
            RamMb: 800, License: "MIT"),
        // The full decoder (32 layers to turbo's 4): on the 22 test phrases 7.1% WER and 19/25 terms against
        // turbo's 9.3% and 17/25, at 0.8 s a phrase on an RTX 3080 to turbo's 0.5 s. Meant for a graphics card.
        new("whisper-large-v3-q5", "Whisper large-v3",
            new SpeechFile(WhisperRepo, WhisperRevision, "ggml-large-v3-q5_0.bin", 1081140203,
                "d75795ecff3f83b5faa89d1900604ad8c780abd5739fae406de19f23ecd98ad1"),
            RamMb: 1900, License: "MIT"),
        new("whisper-small-q5", "Whisper small",
            new SpeechFile(WhisperRepo, WhisperRevision, "ggml-small-q5_1.bin", 190085487,
                "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb"),
            RamMb: 350, License: "MIT"),
    ];

    public static SpeechModel? Find(string? id) =>
        Available.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    public static SpeechModel Default => Find(DefaultId)!;
}
