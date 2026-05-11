using System.Reflection;
using Inferstat;
using Spectre.Console.Cli;

// --help-ai as a global flag, in addition to the help-ai subcommand —
// matches the convention from the ai-ops.dk blog post on agent-friendly CLIs.
if (args.Any(a => a == "--help-ai"))
{
    Console.WriteLine(AgentGuidance.Text);
    return 0;
}

var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "0.0.0-dev";

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("inferstat");
    config.SetApplicationVersion(version);
    config.AddCommand<HealthCommand>("health")
        .WithDescription("Check server health and detect server type (llama.cpp / vLLM / Ollama).")
        .WithExample("health", "http://localhost:8080")
        .WithExample("health", "http://infer:8080", "--json");
    config.AddCommand<ModelsCommand>("models")
        .WithDescription("List loaded/available models with detected family and quantization.")
        .WithExample("models", "http://infer:8080");
    config.AddCommand<SlotsCommand>("slots")
        .WithDescription("Show parallel slot occupancy (llama.cpp specific).")
        .WithExample("slots", "http://infer:8080")
        .WithExample("slots", "http://infer:8080", "--json");
    config.AddCommand<MetricsCommand>("metrics")
        .WithDescription("Read Prometheus-style /metrics endpoint and parse to JSON.")
        .WithExample("metrics", "http://infer:8080", "--json");
    config.AddCommand<HelpAiCommand>("help-ai")
        .WithDescription("Print guidance specifically for AI agents invoking this tool.");
});
return await app.RunAsync(args);
