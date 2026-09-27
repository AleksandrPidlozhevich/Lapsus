namespace Lapsus.Core.Models;

public readonly record struct HuggingFaceModelRef(string Repo, string SubPath)
{
    public static bool TryParse(string? input, out HuggingFaceModelRef reference)
    {
        reference = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var s = input.Trim();

        var hostIdx = s.IndexOf("huggingface.co/", StringComparison.OrdinalIgnoreCase);
        if (hostIdx >= 0)
            s = s[(hostIdx + "huggingface.co/".Length)..];

        var cut = s.IndexOfAny(['?', '#']);
        if (cut >= 0)
            s = s[..cut];

        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        var repo = parts[0] + "/" + parts[1];

        var rest = parts.Skip(2).ToList();
        if (rest.Count >= 2 &&
            (rest[0].Equals("tree", StringComparison.OrdinalIgnoreCase)
             || rest[0].Equals("blob", StringComparison.OrdinalIgnoreCase)
             || rest[0].Equals("resolve", StringComparison.OrdinalIgnoreCase)))
            rest = rest.Skip(2).ToList();

        reference = new HuggingFaceModelRef(repo, string.Join('/', rest));
        return true;
    }
}
