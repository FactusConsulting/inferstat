using Xunit;

namespace Inferstat.Tests;

public class GuessFamilyTests
{
    [Theory]
    [InlineData("gemma-4-26b-q6k", "gemma")]
    [InlineData("Gemma4-E4B-BF16", "gemma")]
    [InlineData("meta-llama-3.3-70b", "llama")]
    [InlineData("mistral-small-3", "mistral")]
    [InlineData("Mistral-Small-Instruct-2409", "mistral")]
    [InlineData("Qwen3-14B-Instruct", "qwen")]
    [InlineData("Phi-3.5-mini-instruct", "phi")]
    [InlineData("gpt-4o-mini", "gpt")]
    [InlineData("claude-sonnet-4-6", "claude")]
    public void GuessFamily_DetectsKnownFamilies(string id, string expected)
    {
        Assert.Equal(expected, Inspector.GuessFamily(id));
    }

    [Theory]
    [InlineData("custom-model-2024")]
    [InlineData("unknown-7b")]
    [InlineData("")]
    public void GuessFamily_ReturnsNull_ForUnknownIds(string id)
    {
        Assert.Null(Inspector.GuessFamily(id));
    }

    [Fact]
    public void GuessFamily_IsCaseInsensitive()
    {
        Assert.Equal("llama", Inspector.GuessFamily("LLAMA-3.3"));
        Assert.Equal("llama", Inspector.GuessFamily("Llama-3.3"));
        Assert.Equal("llama", Inspector.GuessFamily("llama-3.3"));
    }
}

public class GuessQuantTests
{
    [Theory]
    [InlineData("gemma4-26b-q6_k", "Q6_K")]
    [InlineData("gemma4-26b-q4_k_m", "Q4_K_M")]
    [InlineData("gemma4-26b-q5_k_l", "Q5_K_L")]
    [InlineData("gemma4-26b-q8_0", "Q8_0")]
    [InlineData("gemma4-4b-bf16", "BF16")]
    [InlineData("model-fp8", "FP8")]
    [InlineData("model-fp16", "FP16")]
    public void GuessQuant_DetectsKnownQuantizations(string id, string expected)
    {
        Assert.Equal(expected, Inspector.GuessQuant(id));
    }

    [Theory]
    [InlineData("plain-model-name")]
    [InlineData("")]
    public void GuessQuant_ReturnsNull_WhenAbsent(string id)
    {
        Assert.Null(Inspector.GuessQuant(id));
    }
}

public class ParsePrometheusTests
{
    [Fact]
    public void Parse_HandlesBasicMetrics()
    {
        var input = """
            metric_a 42.5
            metric_b 100
            metric_c 0.001
            """;
        var result = Inspector.ParsePrometheus(input);

        Assert.Equal(42.5, result["metric_a"]);
        Assert.Equal(100, result["metric_b"]);
        Assert.Equal(0.001, result["metric_c"]);
    }

    [Fact]
    public void Parse_SkipsCommentsAndEmptyLines()
    {
        var input = """
            # HELP metric_a A test metric
            # TYPE metric_a gauge

            metric_a 1.0

            metric_b 2.0
            """;
        var result = Inspector.ParsePrometheus(input);

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("metric_a"));
        Assert.True(result.ContainsKey("metric_b"));
    }

    [Fact]
    public void Parse_HandlesLabels()
    {
        var input = """
            vllm_running{model="gemma4-26b"} 5
            vllm_waiting{model="gemma4-26b"} 12
            """;
        var result = Inspector.ParsePrometheus(input);

        Assert.Equal(5, result["vllm_running{model=\"gemma4-26b\"}"]);
        Assert.Equal(12, result["vllm_waiting{model=\"gemma4-26b\"}"]);
    }

    [Fact]
    public void Parse_SkipsMalformedLines()
    {
        var input = """
            valid_metric 42
            malformed_line_no_value
            another_valid 100
            """;
        var result = Inspector.ParsePrometheus(input);

        Assert.Equal(2, result.Count);
        Assert.Equal(42, result["valid_metric"]);
        Assert.Equal(100, result["another_valid"]);
    }

    [Fact]
    public void Parse_HandlesScientificNotation()
    {
        var input = "huge_metric 1.5e10";
        var result = Inspector.ParsePrometheus(input);
        Assert.Equal(1.5e10, result["huge_metric"]);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyDict()
    {
        Assert.Empty(Inspector.ParsePrometheus(""));
    }
}
