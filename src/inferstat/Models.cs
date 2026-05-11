using System.Text.Json.Serialization;

namespace Inferstat;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HealthResult))]
[JsonSerializable(typeof(ModelsResult))]
[JsonSerializable(typeof(SlotsResult))]
[JsonSerializable(typeof(MetricsResult))]
[JsonSerializable(typeof(ErrorResult))]
public partial class JsonContext : JsonSerializerContext { }

public record HealthResult(
    string Endpoint,
    string ServerKind,  // llama.cpp, vllm, ollama, openai-compatible, unknown
    bool Ok,
    int? StatusCode,
    long LatencyMs,
    string? Version,
    string? LoadedModel,
    long? UptimeSeconds,
    string? Error);

public record ModelInfo(
    string Id,
    string? Family,
    string? Quantization,
    long? ContextLength,
    long? ParameterCount);

public record ModelsResult(
    string Endpoint,
    int Count,
    ModelInfo[] Models);

public record SlotInfo(
    int Id,
    string State,        // idle, processing
    string? Model,
    long? TokensProcessed,
    long? TokensRemaining,
    long? PromptTokens);

public record SlotsResult(
    string Endpoint,
    int Total,
    int Busy,
    int Idle,
    SlotInfo[] Slots);

public record MetricsResult(
    string Endpoint,
    string ServerKind,
    Dictionary<string, double> Metrics);

public record ErrorResult(string Error, string? Hint);
