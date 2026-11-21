using System;
using System.Collections.Generic;
using System.Linq;

namespace Lapsus.Speech;

// One downloadable file, pinned to a commit and its SHA-256 (from the Hugging Face listing at that commit),
// so what reaches the disk is exactly what was measured.
public sealed record SpeechFile(string Repo, string Revision, string Path, long Size, string Sha256);

// Nemo: NVIDIA NeMo models in onnx-asr's export, the TDT Parakeets and the one-language FastConformer CTCs.
public enum SpeechEngine { Whisper, Nemo }

// Languages: what the model can hear; null means every language Whisper knows.
public sealed record SpeechModel(
    string Id, string DisplayName, IReadOnlyList<SpeechFile> Files, int RamMb, string License,
    SpeechEngine Engine = SpeechEngine.Whisper, IReadOnlySet<string>? Languages = null)
{
    public int SizeMb => (int)(Files.Sum(f => f.Size) / (1024 * 1024));

    public bool Hears(string language) => Languages is null || Languages.Contains(language);
}

// Stage 1 measured large-v3-turbo: q8_0 matches q5_0 for quality and is faster on a CPU, so it is the default.
public static class SpeechModelCatalog
{
    private const string WhisperRepo = "ggerganov/whisper.cpp";
    private const string WhisperRevision = "5359861c739e955e79d9a303bcbc70fb988958b1";

    private const string ParakeetRepo = "istupakov/parakeet-tdt-0.6b-v3-onnx";
    private const string ParakeetRevision = "8f23f0c03c8761650bdb5b40aaf3e40d2c15f1ce";

    public const string DefaultId = "whisper-large-v3-turbo-q8";
    public const string ParakeetId = "parakeet-tdt-v3-int8";

    private const string ParakeetEnglishRepo = "istupakov/parakeet-tdt-0.6b-v2-onnx";
    private const string ParakeetEnglishRevision = "0bbb45a3365852604aef28b538a8f066f4ccaa85";
    public const string ParakeetEnglishId = "parakeet-tdt-v2-en-int8";

    // ivrit.ai publishes only the f16 ggml (1.6 GB); this is its q8_0, byte-identical (same SHA-256) in two
    // independent uploads (this one and tagidu/ivrit-whisper-large-v3-turbo-ggml-quantized).
    private const string HebrewRepo = "sivelsguy/whisper-large-v3-turbo-ivrit-q8_0-ggml";
    private const string HebrewRevision = "cb0198e7a043608f6a48ccfe41cfcc8de5321b95";
    public const string HebrewId = "whisper-large-v3-turbo-he-q8";

    // NVIDIA's one-language FastConformer CTC models (115M), exported by OpenVoiceOS. Their int8 export does not run
    // (ConvInteger has no CPU kernel), so it is fp32. Whisper is no use here: on 15 FLEURS phrases each, turbo's ka
    // WER was 100% (it writes something else), where these took ka 16.6% and hy 8.9% at ~0.2 s a phrase on the CPU.
    private const string GeorgianRepo = "OpenVoiceOS/stt_ka_fastconformer_hybrid_large_pc_onnx";
    private const string GeorgianRevision = "fa19e061165a0238fe6477c65cdc3357d56f5788";
    public const string GeorgianId = "fastconformer-ka";
    private const string ArmenianRepo = "OpenVoiceOS/stt_hy_fastconformer_hybrid_large_pc_onnx";
    private const string ArmenianRevision = "f7db1fad9af6bd8a7a9e08fca02068c403b35468";
    public const string ArmenianId = "fastconformer-hy";

    // Silero VAD 6.2 for the CTC models, which write words over silence (NemoRecognizer); each folder takes a copy.
    private static readonly SpeechFile OnnxVad = new(
        "istupakov/silero-vad-onnx", "b3e3ee3cce4c11ceb63b1a0b229d916069c1ddf6", "silero_vad.onnx", 2327524,
        "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3");

    private static readonly HashSet<string> EnglishOnly = new(StringComparer.OrdinalIgnoreCase) { "en" };
    private static readonly HashSet<string> HebrewOnly = new(StringComparer.OrdinalIgnoreCase) { "he" };
    private static readonly HashSet<string> GeorgianOnly = new(StringComparer.OrdinalIgnoreCase) { "ka" };
    private static readonly HashSet<string> ArmenianOnly = new(StringComparer.OrdinalIgnoreCase) { "hy" };

    // Where the main layout is one of these, its own model is the suggestion, graphics card or not.
    private static readonly Dictionary<string, string> ForMainLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["he"] = HebrewId, ["ka"] = GeorgianId, ["hy"] = ArmenianId,
    };

    public static readonly SpeechFile Vad = new(
        "ggml-org/whisper-vad", "9ffd54a1e1ee413ddf265af9913beaf518d1639b", "ggml-silero-v6.2.0.bin",
        885098, "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987");

    // Parakeet TDT 0.6B v3's 25 European languages.
    private static readonly HashSet<string> ParakeetLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "bg", "cs", "da", "de", "el", "en", "es", "et", "fi", "fr", "hr", "hu", "it", "lt", "lv", "mt", "nl",
        "pl", "pt", "ro", "ru", "sk", "sl", "sv", "uk",
    };

    public static readonly IReadOnlyList<SpeechModel> Available =
    [
        new(DefaultId, "Whisper large-v3-turbo",
            [new SpeechFile(WhisperRepo, WhisperRevision, "ggml-large-v3-turbo-q8_0.bin", 874188075,
                "317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1")],
            RamMb: 1100, License: "MIT"),
        // On the 22 recorded ru phrases (i7-9700F, 4 threads): 272 ms a phrase to turbo's 2461 ms on the same CPU,
        // but 12.6% WER and 12/25 terms to turbo's 6.6% and 22/25 — English terms come out in Cyrillic. Everyday
        // speech is near perfect. So it is the pick without a graphics card, not with one.
        new(ParakeetId, "Parakeet TDT v3",
            [
                new SpeechFile(ParakeetRepo, ParakeetRevision, "encoder-model.int8.onnx", 652183999,
                    "6139d2fa7e1b086097b277c7149725edbab89cc7c7ae64b23c741be4055aff09"),
                new SpeechFile(ParakeetRepo, ParakeetRevision, "decoder_joint-model.int8.onnx", 18202004,
                    "eea7483ee3d1a30375daedc8ed83e3960c91b098812127a0d99d1c8977667a70"),
                new SpeechFile(ParakeetRepo, ParakeetRevision, "nemo128.onnx", 139764,
                    "a9fde1486ebfcc08f328d75ad4610c67835fea58c73ba57e3209a6f6cf019e9f"),
                // Not in LFS, so the listing has no SHA-256; this one is of the file at that commit.
                new SpeechFile(ParakeetRepo, ParakeetRevision, "vocab.txt", 93939,
                    "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d"),
            ],
            RamMb: 900, License: "CC-BY-4.0", Engine: SpeechEngine.Nemo, Languages: ParakeetLanguages),
        // English only, and NVIDIA's most accurate English model (v3 trades some of that for 24 more languages).
        // Same export and decoder as v3; 295 ms on a 4 s English sample (i7-9700F, 4 threads).
        new(ParakeetEnglishId, "Parakeet TDT v2",
            [
                new SpeechFile(ParakeetEnglishRepo, ParakeetEnglishRevision, "encoder-model.int8.onnx", 652184014,
                    "3e0581fda6ab843888b51e56d7ee78b6d5bc3237ec113af1f732d1d5286aa155"),
                new SpeechFile(ParakeetEnglishRepo, ParakeetEnglishRevision, "decoder_joint-model.int8.onnx", 8998286,
                    "a449f49acd68979d418651dd2dcb737cc0f1bf0225e009e29ee326354edbf7d3"),
                new SpeechFile(ParakeetEnglishRepo, ParakeetEnglishRevision, "nemo128.onnx", 139764,
                    "a9fde1486ebfcc08f328d75ad4610c67835fea58c73ba57e3209a6f6cf019e9f"),
                // Not in LFS, so the listing has no SHA-256; this one is of the file at that commit.
                new SpeechFile(ParakeetEnglishRepo, ParakeetEnglishRevision, "vocab.txt", 9384,
                    "ec182b70dd42113aff6c5372c75cac58c952443eb22322f57bbd7f53977d497d"),
            ],
            RamMb: 900, License: "CC-BY-4.0", Engine: SpeechEngine.Nemo, Languages: EnglishOnly),
        // ivrit.ai's Hebrew fine-tune of turbo. Its language detection is broken by the training, so every phrase
        // is read as Hebrew (DictationEngine pins a one-language model), so it is suggested only where Hebrew
        // is the main layout.
        // 15 KAN broadcast phrases: WER 7.8% on an RTX 3080 / 8.7% on the CPU, to stock turbo's 13.9% / 28.7%,
        // at the same speed.
        new(HebrewId, "Whisper large-v3-turbo Hebrew (ivrit.ai)",
            [new SpeechFile(HebrewRepo, HebrewRevision, "ggml-ivrit-large-v3-turbo-q8_0.bin", 874188075,
                "123a936e686b06d45b52dc1790251a1418841352ca94079ae643d57893ffc9a6")],
            RamMb: 1100, License: "Apache-2.0", Languages: HebrewOnly),
        new(GeorgianId, "NVIDIA FastConformer Georgian",
            [
                new SpeechFile(GeorgianRepo, GeorgianRevision, "model.onnx", 458160971,
                    "48b5224bdcc229f2eb0ef8175eb0314fd6181f88073ac8c3a9a7201779916764"),
                // Not in LFS, so the listing has no SHA-256; this one is of the file at that commit.
                new SpeechFile(GeorgianRepo, GeorgianRevision, "vocab.txt", 23251,
                    "a56cd6fbb72e8666ae3f8aab3752b3637a11a97de56724bfa25ba3915dd2f988"),
                OnnxVad,
            ],
            RamMb: 700, License: "CC-BY-4.0", Engine: SpeechEngine.Nemo, Languages: GeorgianOnly),
        new(ArmenianId, "NVIDIA FastConformer Armenian",
            [
                new SpeechFile(ArmenianRepo, ArmenianRevision, "model.onnx", 458160971,
                    "09ed4278c8536193f5324ae5b1e21be3ef1abaa92779e8c7b4577644071b131a"),
                new SpeechFile(ArmenianRepo, ArmenianRevision, "vocab.txt", 12946,
                    "221b1dd978a2d8f4fe8a8e185e400c09f98d71c887de9099cb2f1154d321416b"),
                OnnxVad,
            ],
            RamMb: 700, License: "CC-BY-4.0", Engine: SpeechEngine.Nemo, Languages: ArmenianOnly),
        new("whisper-large-v3-turbo-q5", "Whisper large-v3-turbo q5",
            [new SpeechFile(WhisperRepo, WhisperRevision, "ggml-large-v3-turbo-q5_0.bin", 574041195,
                "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2")],
            RamMb: 800, License: "MIT"),
        // The full decoder (32 layers to turbo's 4): on the 22 test phrases 7.1% WER and 19/25 terms against
        // turbo's 9.3% and 17/25, at 0.8 s a phrase on an RTX 3080 to turbo's 0.5 s. Meant for a graphics card.
        new("whisper-large-v3-q5", "Whisper large-v3",
            [new SpeechFile(WhisperRepo, WhisperRevision, "ggml-large-v3-q5_0.bin", 1081140203,
                "d75795ecff3f83b5faa89d1900604ad8c780abd5739fae406de19f23ecd98ad1")],
            RamMb: 1900, License: "MIT"),
        new("whisper-small-q5", "Whisper small",
            [new SpeechFile(WhisperRepo, WhisperRevision, "ggml-small-q5_1.bin", 190085487,
                "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb")],
            RamMb: 350, License: "MIT"),
    ];

    public static SpeechModel? Find(string? id) =>
        Available.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    public static SpeechModel Default => Find(DefaultId)!;

    // The model a dictation in `language` runs on: a downloaded model made for that language alone (turbo "hears"
    // Georgian, but writes something else), else the chosen one if it hears it, else the downloaded model that hears
    // it most narrowly (Parakeet v3's 25 before Whisper's all), else the chosen one.
    public static SpeechModel ForLanguage(SpeechModel chosen, string language, Func<SpeechModel, bool> installed)
    {
        bool Have(SpeechModel m) => m == chosen || installed(m);
        return Available.FirstOrDefault(m => m.Languages is { Count: 1 } && m.Hears(language) && Have(m))
               ?? (chosen.Hears(language)
                   ? chosen
                   : Available.Where(m => m.Hears(language) && installed(m))
                         .OrderBy(m => m.Languages?.Count ?? int.MaxValue)
                         .FirstOrDefault() ?? chosen);
    }

    // The model made for this language, where Whisper is weak in it (he, ka, hy).
    public static SpeechModel? OwnModel(string language) =>
        ForMainLanguage.TryGetValue(language, out var id) ? Find(id) : null;

    // Without a graphics card turbo takes ~2.5 s a phrase, Parakeet ~0.3 s — but only if it hears every language
    // the user writes in (their installed layouts); otherwise Whisper, which hears them all. English alone gets v2.
    // The main language is the first layout's.
    public static SpeechModel Recommended(bool hasGpu, IReadOnlyCollection<string> languages)
    {
        if (languages.FirstOrDefault() is { } main && ForMainLanguage.TryGetValue(main, out var own))
            return Find(own)!;
        if (hasGpu || languages.Count == 0)
            return Default;
        return new[] { Find(ParakeetEnglishId)!, Find(ParakeetId)! }.FirstOrDefault(m => languages.All(m.Hears)) ?? Default;
    }
}
