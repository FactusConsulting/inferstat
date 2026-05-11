using System.ComponentModel;
using Spectre.Console.Cli;

namespace Inferstat;

public class GlobalSettings : CommandSettings
{
    [CommandOption("--json")]
    [Description("Emit machine-readable JSON to stdout.")]
    public bool Json { get; init; }

    [CommandOption("--quiet")]
    [Description("Minimal output (status only).")]
    public bool Quiet { get; init; }

    [CommandOption("--timeout <SECONDS>")]
    [DefaultValue(15)]
    [Description("HTTP timeout in seconds.")]
    public int TimeoutSeconds { get; init; } = 15;

    [CommandOption("--api-key <TOKEN>")]
    [Description("Bearer token (falls back to OPENAI_API_KEY env var).")]
    public string? ApiKey { get; init; }

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
    public string ResolvedApiKey() => ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
    public void ApplyToRender() { Render.Format = Json ? OutputFormat.Json : OutputFormat.Text; Render.Quiet = Quiet; }
}

public class EndpointSettings : GlobalSettings
{
    [CommandArgument(0, "<endpoint>")]
    [Description("Inference server base URL (e.g. http://infer:8080 for llama.cpp).")]
    public required string Endpoint { get; init; }
}

public sealed class HealthCommand : AsyncCommand<EndpointSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, EndpointSettings s)
    {
        s.ApplyToRender();
        using var http = Inspector.CreateClient(s.ResolvedApiKey(), s.Timeout);
        var r = await Inspector.HealthAsync(http, s.Endpoint, default);
        Render.Health(r);
        return r.Ok ? 0 : 74;
    }
}

public sealed class ModelsCommand : AsyncCommand<EndpointSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, EndpointSettings s)
    {
        s.ApplyToRender();
        using var http = Inspector.CreateClient(s.ResolvedApiKey(), s.Timeout);
        var r = await Inspector.ModelsAsync(http, s.Endpoint, default);
        if (r == null) { Render.Error("models endpoint unreachable or non-200", $"Try: inferstat health {s.Endpoint}"); return 74; }
        Render.Models(r);
        return 0;
    }
}

public sealed class SlotsCommand : AsyncCommand<EndpointSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, EndpointSettings s)
    {
        s.ApplyToRender();
        using var http = Inspector.CreateClient(s.ResolvedApiKey(), s.Timeout);
        var r = await Inspector.SlotsAsync(http, s.Endpoint, default);
        if (r == null) { Render.Error("slots endpoint unavailable", "llama.cpp exposes /slots. vLLM and Ollama do not."); return 74; }
        Render.Slots(r);
        return 0;
    }
}

public sealed class MetricsCommand : AsyncCommand<EndpointSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext ctx, EndpointSettings s)
    {
        s.ApplyToRender();
        using var http = Inspector.CreateClient(s.ResolvedApiKey(), s.Timeout);
        var r = await Inspector.MetricsAsync(http, s.Endpoint, default);
        if (r == null) { Render.Error("metrics endpoint unavailable", "Try: inferstat health to identify the server type."); return 74; }
        Render.Metrics(r);
        return 0;
    }
}

public sealed class HelpAiCommand : Command
{
    public override int Execute(CommandContext ctx)
    {
        Console.WriteLine("""
            inferstat — guidance for AI agents

            WHEN TO USE
              Inspect a running LLM inference server (llama.cpp, vLLM, Ollama) to
              find out what model is loaded, how many slots are busy, and what
              metrics are being emitted. Use before sending production traffic.

            SAFE BY DEFAULT
              All commands are read-only HTTP GET. No state mutation. Safe to call
              from a monitoring loop.

            AUTHENTICATION
              For secured endpoints (vLLM started with --api-key, gateways, etc.):
                - Pass --api-key <token>, OR
                - Set OPENAI_API_KEY in the environment (used as generic bearer token)
              Local llama.cpp/Ollama instances typically need no authentication.

            PREFERRED PATTERNS
              - Use 'health' first to determine the server type (llama.cpp/vllm/ollama)
              - 'slots' is llama.cpp-specific; vLLM/Ollama will return 74
              - 'metrics' returns Prometheus exposition format parsed to JSON

            EXIT CODES
              0   success
              74  endpoint or sub-endpoint unreachable (transient)
              78  configuration error
              1   unexpected error

            OUTPUT SCHEMA
              --json emits a flat record per invocation with stable snake_case fields.
              Errors go to stderr as {"error","hint"}.

            EXAMPLES
              inferstat health http://infer:8080 --json | jq .server_kind
              inferstat slots http://infer:8080 --json | jq '.busy,.total'
              inferstat metrics http://infer:8080 --json | jq '.metrics | with_entries(select(.key | startswith("vllm_")))'
            """);
        return 0;
    }
}
