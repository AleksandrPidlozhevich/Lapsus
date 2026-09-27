using Lapsus.Core.Models;

namespace Lapsus.Core.Tests;

public class HuggingFaceModelRefTests
{
    [Fact]
    public void Parses_bare_repo_with_no_subfolder()
    {
        Assert.True(HuggingFaceModelRef.TryParse("microsoft/Phi-3.5-mini-instruct-onnx", out var r));
        Assert.Equal("microsoft/Phi-3.5-mini-instruct-onnx", r.Repo);
        Assert.Equal("", r.SubPath);
    }

    [Fact]
    public void Parses_repo_with_subfolder()
    {
        Assert.True(HuggingFaceModelRef.TryParse("org/name/cpu_and_mobile/cpu-int4", out var r));
        Assert.Equal("org/name", r.Repo);
        Assert.Equal("cpu_and_mobile/cpu-int4", r.SubPath);
    }

    [Fact]
    public void Parses_full_url_with_tree_ref()
    {
        Assert.True(HuggingFaceModelRef.TryParse(
            "https://huggingface.co/microsoft/Phi-3.5-mini-instruct-onnx/tree/main/directml/directml-int4-awq-block-128",
            out var r));
        Assert.Equal("microsoft/Phi-3.5-mini-instruct-onnx", r.Repo);
        Assert.Equal("directml/directml-int4-awq-block-128", r.SubPath);
    }

    [Fact]
    public void Parses_full_url_with_blob_ref()
    {
        Assert.True(HuggingFaceModelRef.TryParse(
            "http://www.huggingface.co/org/name/blob/main/cpu_and_mobile", out var r));
        Assert.Equal("org/name", r.Repo);
        Assert.Equal("cpu_and_mobile", r.SubPath);
    }

    [Fact]
    public void Parses_url_at_repo_root()
    {
        Assert.True(HuggingFaceModelRef.TryParse("https://huggingface.co/org/name", out var r));
        Assert.Equal("org/name", r.Repo);
        Assert.Equal("", r.SubPath);
    }

    [Fact]
    public void Strips_query_and_fragment()
    {
        Assert.True(HuggingFaceModelRef.TryParse("org/name/sub?foo=bar#frag", out var r));
        Assert.Equal("org/name", r.Repo);
        Assert.Equal("sub", r.SubPath);
    }

    [Fact]
    public void Trims_whitespace_and_ignores_surrounding_slashes()
    {
        Assert.True(HuggingFaceModelRef.TryParse("  /org/name/  ", out var r));
        Assert.Equal("org/name", r.Repo);
        Assert.Equal("", r.SubPath);
    }

    [Fact]
    public void A_bare_tree_ref_without_a_subfolder_leaves_subpath_empty()
    {
        Assert.True(HuggingFaceModelRef.TryParse("org/name/tree/main", out var r));
        Assert.Equal("org/name", r.Repo);
        Assert.Equal("", r.SubPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("justname")]
    [InlineData("https://huggingface.co/onlyorg")]
    public void Rejects_input_without_org_and_name(string? input)
    {
        Assert.False(HuggingFaceModelRef.TryParse(input, out var r));
        Assert.Equal(default, r);
    }
}
