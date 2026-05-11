using System.Text.Json;
using Spectre.Console;

namespace Inferstat;

public enum OutputFormat { Text, Json }

public static class Render
{
    public static OutputFormat Format { get; set; } = OutputFormat.Text;
    public static bool Quiet { get; set; } = false;

    public static void Health(HealthResult r)
    {
        if (Format == OutputFormat.Json) { Console.WriteLine(JsonSerializer.Serialize(r, JsonContext.Default.HealthResult)); return; }
        if (Quiet) { Console.WriteLine(r.Ok ? "ok" : "fail"); return; }
        var status = r.Ok ? "[green]✓[/]" : "[red]✗[/]";
        AnsiConsole.MarkupLineInterpolated($"{status} [bold]{r.Endpoint}[/]  [grey]({r.ServerKind})[/]");
        var t = new Table().Border(TableBorder.Minimal).HideHeaders().AddColumn("k").AddColumn("v");
        t.AddRow("status", r.StatusCode?.ToString() ?? "—");
        t.AddRow("latency", $"{r.LatencyMs} ms");
        if (r.Version != null) t.AddRow("version", Markup.Escape(r.Version));
        if (r.LoadedModel != null) t.AddRow("loaded model", $"[yellow]{Markup.Escape(r.LoadedModel)}[/]");
        if (r.UptimeSeconds.HasValue) t.AddRow("uptime", FormatDuration(r.UptimeSeconds.Value));
        if (r.Error != null) t.AddRow("[red]error[/]", Markup.Escape(r.Error));
        AnsiConsole.Write(t);
    }

    public static void Models(ModelsResult r)
    {
        if (Format == OutputFormat.Json) { Console.WriteLine(JsonSerializer.Serialize(r, JsonContext.Default.ModelsResult)); return; }
        if (Quiet) { foreach (var m in r.Models) Console.WriteLine(m.Id); return; }
        AnsiConsole.MarkupLineInterpolated($"[bold]{r.Count}[/] model(s) on [cyan]{r.Endpoint}[/]");
        var t = new Table().Border(TableBorder.Rounded);
        t.AddColumn("Model").AddColumn("Family").AddColumn("Quant");
        foreach (var m in r.Models)
            t.AddRow(Markup.Escape(m.Id), m.Family ?? "[grey]?[/]", m.Quantization ?? "[grey]?[/]");
        AnsiConsole.Write(t);
    }

    public static void Slots(SlotsResult r)
    {
        if (Format == OutputFormat.Json) { Console.WriteLine(JsonSerializer.Serialize(r, JsonContext.Default.SlotsResult)); return; }
        if (Quiet) { Console.WriteLine($"{r.Busy}/{r.Total}"); return; }
        AnsiConsole.MarkupLineInterpolated($"Slots on [cyan]{r.Endpoint}[/] — [yellow]{r.Busy}[/] busy, [green]{r.Idle}[/] idle (total {r.Total})");
        var t = new Table().Border(TableBorder.Rounded);
        t.AddColumn("ID").AddColumn("State").AddColumn("Processed").AddColumn("Remaining").AddColumn("Prompt tokens");
        foreach (var s in r.Slots)
        {
            var stateMarkup = s.State == "processing" ? "[yellow]processing[/]" : "[green]idle[/]";
            t.AddRow(s.Id.ToString(), stateMarkup, s.TokensProcessed?.ToString() ?? "—",
                s.TokensRemaining?.ToString() ?? "—", s.PromptTokens?.ToString() ?? "—");
        }
        AnsiConsole.Write(t);
    }

    public static void Metrics(MetricsResult r)
    {
        if (Format == OutputFormat.Json) { Console.WriteLine(JsonSerializer.Serialize(r, JsonContext.Default.MetricsResult)); return; }
        AnsiConsole.MarkupLineInterpolated($"Metrics from [cyan]{r.Endpoint}[/] [grey]({r.ServerKind})[/]");
        if (r.Metrics.Count == 0) { AnsiConsole.MarkupLine("[grey]no metrics returned[/]"); return; }
        var t = new Table().Border(TableBorder.Rounded);
        t.AddColumn("Metric").AddColumn(new TableColumn("Value").RightAligned());
        foreach (var kv in r.Metrics.OrderBy(k => k.Key))
            t.AddRow(Markup.Escape(kv.Key), kv.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        AnsiConsole.Write(t);
    }

    public static void Error(string err, string? hint = null)
    {
        if (Format == OutputFormat.Json)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new ErrorResult(err, hint), JsonContext.Default.ErrorResult));
            return;
        }
        AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {err}");
        if (hint != null) AnsiConsole.MarkupLineInterpolated($"[grey]hint:[/]  {hint}");
    }

    private static string FormatDuration(long seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60}m {seconds % 60}s";
        if (seconds < 86400) return $"{seconds / 3600}h {(seconds % 3600) / 60}m";
        return $"{seconds / 86400}d {(seconds % 86400) / 3600}h";
    }
}
