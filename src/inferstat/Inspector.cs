using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Inferstat;

public static class Inspector
{
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
        // Try llama.cpp /health first, then /v1/models for vLLM/Ollama
        string serverKind = "unknown";
        string? version = null;
        string? loadedModel = null;
        long? uptime = null;
        int? status = null;

        try
        {
            using var res = await http.GetAsync($"{e}/health", ct);
            status = (int)res.StatusCode;
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                if (body.Contains("\"status\"", StringComparison.OrdinalIgnoreCase))
                    serverKind = "llama.cpp";
                else if (body.Contains("\"version\"", StringComparison.OrdinalIgnoreCase))
                    serverKind = "ollama";
            }
        }
        catch { }

        try
        {
            using var pres = await http.GetAsync($"{e}/v1/internal/server_props", ct);
            if (pres.IsSuccessStatusCode) { serverKind = "llama.cpp"; }
        }
        catch { }

        try
        {
            using var vres = await http.GetAsync($"{e}/version", ct);
            if (vres.IsSuccessStatusCode)
            {
                var json = await vres.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("version", out var v))
                {
                    version = v.GetString();
                    serverKind = "ollama";
                }
            }
        }
        catch { }

        try
        {
            using var pres = await http.GetAsync($"{e}/props", ct);
            if (pres.IsSuccessStatusCode)
            {
                serverKind = "llama.cpp";
                var json = await pres.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("default_generation_settings", out var dgs)
                    && dgs.TryGetProperty("model", out var m))
                    loadedModel = m.GetString();
            }
        }
        catch { }

        sw.Stop();
        var ok = status.HasValue && status.Value < 500;
        return new HealthResult(e, serverKind, ok, status, sw.ElapsedMilliseconds, version, loadedModel, uptime,
            ok ? null : "no /health or /version response");
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

    public static async Task<SlotsResult?> SlotsAsync(HttpClient http, string endpoint, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        try
        {
            using var res = await http.GetAsync($"{e}/slots", ct);
            if (!res.IsSuccessStatusCode) return null;
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
            return new SlotsResult(e, list.Count, busy, list.Count - busy, list.ToArray());
        }
        catch { return null; }
    }

    public static async Task<MetricsResult?> MetricsAsync(HttpClient http, string endpoint, CancellationToken ct)
    {
        var e = Normalize(endpoint);
        try
        {
            using var res = await http.GetAsync($"{e}/metrics", ct);
            if (!res.IsSuccessStatusCode) return null;
            var text = await res.Content.ReadAsStringAsync(ct);
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
            return new MetricsResult(e, "prometheus-exposition", dict);
        }
        catch { return null; }
    }

    private static string? GuessFamily(string id)
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

    private static string? GuessQuant(string id)
    {
        var lower = id.ToLowerInvariant();
        string[] quants = ["q2_k", "q3_k", "q4_0", "q4_1", "q4_k_s", "q4_k_m", "q4_k_l", "q5_0", "q5_k_s", "q5_k_m", "q5_k_l", "q6_k", "q8_0", "fp8", "bf16", "fp16"];
        foreach (var q in quants) if (lower.Contains(q)) return q.ToUpperInvariant();
        return null;
    }
}
