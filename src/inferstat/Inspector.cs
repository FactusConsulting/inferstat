using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Inferstat;

public static class Inspector
{
    private const string LlamaCpp = "llama.cpp";

    public static HttpClient CreateClient(string? apiKey, TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("inferstat/0.1");
        if (!string.IsNullOrWhiteSpace(apiKey))
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return c;
    }

    public static string Normalize(string endpoint)
    {
        var e = endpoint.TrimEnd('/');
        if (!e.StartsWith("http", StringComparison.OrdinalIgnoreCase)) e = "http://" + e;
        return e;
    }

    public static async Task<HealthResult> HealthAsync(HttpClient http, string endpoint, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        var sw = Stopwatch.StartNew();
        // Detect the server kind from the most specific signal available. Probes
        // run weakest-first; a later, more-specific match overrides an earlier guess.
        string serverKind = "unknown";
        string? version = null;
        string? loadedModel = null;
        long? uptime = null;
        int? status = null;

        // Liveness, plus a weak llama.cpp hint (its /health returns {"status": "ok"}).
        try
        {
            using var res = await http.GetAsync($"{e}/health", ct);
            status = (int)res.StatusCode;
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                if (body.Contains("\"status\"", StringComparison.OrdinalIgnoreCase))
                    serverKind = LlamaCpp;
            }
        }
        catch { /* endpoint absent or unreachable; try the next signal */ }

        // /v1/models — any OpenAI-compatible server. Drives the "openai-compatible"
        // fallback (e.g. a LiteLLM gateway) and captures the served model for every
        // server kind. A 401/403 just means we can't read it without a key.
        try
        {
            using var lres = await http.GetAsync($"{e}/v1/models", ct);
            if (lres.IsSuccessStatusCode)
            {
                if (serverKind == "unknown") serverKind = "openai-compatible";
                var json = await lres.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0
                    && data[0].TryGetProperty("id", out var id))
                    loadedModel = id.GetString();
            }
        }
        catch { /* endpoint absent or unreachable; try the next signal */ }

        // /version — vLLM (and other FastAPI servers) expose {"version"} here.
        // Ollama does NOT (it uses /api/version), so this is purely a version-string
        // capture; the server *kind* is decided by the more specific probes below.
        try
        {
            using var vres = await http.GetAsync($"{e}/version", ct);
            if (vres.IsSuccessStatusCode)
            {
                var json = await vres.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("version", out var v))
                    version = v.GetString();
            }
        }
        catch { /* endpoint absent or unreachable; try the next signal */ }

        // /metrics prefix — the most reliable discriminator: vLLM emits `vllm:`
        // series, llama.cpp emits `llamacpp:` series.
        try
        {
            using var mres = await http.GetAsync($"{e}/metrics", ct);
            if (mres.IsSuccessStatusCode)
            {
                var body = await mres.Content.ReadAsStringAsync(ct);
                if (body.Contains("vllm:", StringComparison.Ordinal)) serverKind = "vllm";
                else if (body.Contains("llamacpp:", StringComparison.Ordinal)) serverKind = LlamaCpp;
            }
        }
        catch { /* endpoint absent or unreachable; try the next signal */ }

        // Ollama-specific: only Ollama serves /api/version.
        try
        {
            using var ares = await http.GetAsync($"{e}/api/version", ct);
            if (ares.IsSuccessStatusCode)
            {
                var json = await ares.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("version", out var v))
                {
                    version = v.GetString();
                    serverKind = "ollama";
                }
            }
        }
        catch { /* endpoint absent or unreachable; try the next signal */ }

        // llama.cpp definitive: server_props + /props (the latter carries the model).
        try
        {
            using var pres = await http.GetAsync($"{e}/v1/internal/server_props", ct);
            if (pres.IsSuccessStatusCode) serverKind = LlamaCpp;
        }
        catch { /* endpoint absent or unreachable; try the next signal */ }

        try
        {
            using var pres = await http.GetAsync($"{e}/props", ct);
            if (pres.IsSuccessStatusCode)
            {
                serverKind = LlamaCpp;
                var json = await pres.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("default_generation_settings", out var dgs)
                    && dgs.TryGetProperty("model", out var m))
                    loadedModel = m.GetString();
            }
        }
        catch { /* endpoint absent or unreachable; try the next signal */ }

        sw.Stop();
        var ok = status.HasValue && status.Value < 500;
        return new HealthResult(e, serverKind, ok, status, sw.ElapsedMilliseconds, version, loadedModel, uptime,
            ok ? null : "no /health response");
    }

    public static async Task<ModelsResult?> ModelsAsync(HttpClient http, string endpoint, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        try
        {
            using var res = await http.GetAsync($"{e}/v1/models", ct);
            if (!res.IsSuccessStatusCode) return null;
            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            var list = new List<ModelInfo>();
            foreach (var item in data.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(id)) continue;
                list.Add(new ModelInfo(id, GuessFamily(id), GuessQuant(id), null, null));
            }
            return new ModelsResult(e, list.Count, list.ToArray());
        }
        catch { return null; }
    }

    public static async Task<(SlotsResult? Result, int StatusCode)> SlotsAsync(HttpClient http, string endpoint, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        try
        {
            using var res = await http.GetAsync($"{e}/slots", ct);
            if (!res.IsSuccessStatusCode) return (null, (int)res.StatusCode);
            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var list = new List<SlotInfo>();
            foreach (var slot in doc.RootElement.EnumerateArray())
            {
                var id = slot.TryGetProperty("id", out var idP) ? idP.GetInt32() : 0;
                var state = slot.TryGetProperty("state", out var stP) ? stP.ToString() : "?";
                string stateStr = state switch { "0" => "idle", "1" => "processing", _ => state };
                long? processed = slot.TryGetProperty("n_decoded", out var p) ? p.GetInt64() : null;
                long? remaining = slot.TryGetProperty("n_remaining", out var r) ? r.GetInt64() : null;
                long? promptT = slot.TryGetProperty("n_prompt_tokens", out var pt) ? pt.GetInt64() : null;
                list.Add(new SlotInfo(id, stateStr, null, processed, remaining, promptT));
            }
            var busy = list.Count(s => s.State == "processing");
            return (new SlotsResult(e, list.Count, busy, list.Count - busy, list.ToArray()), 200);
        }
        catch { return (null, 0); }
    }

    public static async Task<(MetricsResult? Result, int StatusCode)> MetricsAsync(HttpClient http, string endpoint, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        try
        {
            using var res = await http.GetAsync($"{e}/metrics", ct);
            if (!res.IsSuccessStatusCode) return (null, (int)res.StatusCode);
            var text = await res.Content.ReadAsStringAsync(ct);
            return (new MetricsResult(e, "prometheus-exposition", ParsePrometheus(text)), 200);
        }
        catch { return (null, 0); }
    }

    internal static Dictionary<string, double> ParsePrometheus(string text)
    {
        var dict = new Dictionary<string, double>();
        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var idx = line.LastIndexOf(' ');
            if (idx < 0) continue;
            var key = line[..idx].Trim();
            if (double.TryParse(line[(idx + 1)..].Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var val))
                dict[key] = val;
        }
        return dict;
    }

    internal static string? GuessFamily(string id)
    {
        var lower = id.ToLowerInvariant();
        if (lower.Contains("gemma")) return "gemma";
        if (lower.Contains("llama")) return "llama";
        if (lower.Contains("mistral")) return "mistral";
        if (lower.Contains("qwen")) return "qwen";
        if (lower.Contains("phi")) return "phi";
        if (lower.Contains("gpt")) return "gpt";
        if (lower.Contains("claude")) return "claude";
        return null;
    }

    internal static string? GuessQuant(string id)
    {
        var lower = id.ToLowerInvariant();
        string[] quants = ["q2_k", "q3_k", "q4_0", "q4_1", "q4_k_s", "q4_k_m", "q4_k_l", "q5_0", "q5_k_s", "q5_k_m", "q5_k_l", "q6_k", "q8_0", "fp8", "bf16", "fp16"];
        foreach (var q in quants) if (lower.Contains(q)) return q.ToUpperInvariant();
        return null;
    }
}
