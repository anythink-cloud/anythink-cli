using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Models;

public sealed record ChartInput(
    string? Name = null, string? Entity = null, string? Type = null, string? Source = null,
    string? GroupBy = null, string? Interval = null, string? Measure = null, string? Agg = null, string? StackBy = null,
    IReadOnlyList<string>? Filters = null, string? Timeframe = null, string? From = null, string? To = null,
    int? Limit = null, string? Sort = null, bool? Cumulative = null, decimal? Target = null, string? TargetMode = null,
    bool? Compare = null, int? CompareDays = null, bool? CompareOverlay = null,
    string? PlatformMetric = null, string? PlatformPeriod = null, string? QuotaMetric = null, string? Config = null);

public sealed record ChartBuild(JsonObject? Body, IReadOnlyList<string> Problems)
{
    public bool Ok => Body is not null && Problems.Count == 0;
}

public static class ChartVocabulary
{
    public static readonly string[] ChartTypes = ["column", "line", "pie", "area", "stat", "gauge", "funnel"];
    public static readonly string[] Sources = ["entity", "platform", "quota"];
    public static readonly string[] Aggregations = ["count", "sum", "avg", "min", "max"];
    public static readonly string[] Intervals = ["hour", "day", "week", "month", "quarter", "year"];
    public static readonly string[] Sorts = ["asc", "desc"];
    public static readonly string[] Timeframes = ["all", "24h", "7d", "30d", "90d", "365d", "custom"];
    public static readonly string[] TargetModes = ["goal", "warning"];
    public static readonly string[] PlatformPeriods = ["day", "week", "month", "quarter", "year"];
    public static readonly string[] FunnelStyles = ["vertical", "horizontal", "sankey"];
    public static readonly string[] FunnelModes = ["rows", "cohort"];
    public static readonly string[] FilterOperators = ["eq", "ne", "gt", "gte", "lt", "lte", "contains", "in", "exists", "not_exists"];

    internal static readonly string[] WithoutValue = ["exists", "not_exists"];

    public static string List(IEnumerable<string> values) => string.Join(", ", values);
}

public static class ChartSpec
{
    private enum Kind { Text, Whole, Number, Flag, List, Moment }

    private static readonly Dictionary<string, Kind> Settings = new(StringComparer.Ordinal)
    {
        ["name"] = Kind.Text, ["dataSource"] = Kind.Text, ["chartType"] = Kind.Text, ["entityName"] = Kind.Text,
        ["xField"] = Kind.Text, ["yField"] = Kind.Text, ["pieMode"] = Kind.Text, ["platformMetric"] = Kind.Text,
        ["platformPeriod"] = Kind.Text, ["quotaMetric"] = Kind.Text, ["stackBy"] = Kind.Text, ["yAgg"] = Kind.Text,
        ["xInterval"] = Kind.Text, ["timeframePreset"] = Kind.Text, ["timeframeFrom"] = Kind.Moment,
        ["timeframeTo"] = Kind.Moment, ["timeframeOffsetDays"] = Kind.Whole, ["compareEnabled"] = Kind.Flag,
        ["comparePreviousDays"] = Kind.Whole, ["compareShowOverlay"] = Kind.Flag, ["limit"] = Kind.Whole,
        ["sortDirection"] = Kind.Text, ["cumulative"] = Kind.Flag, ["target"] = Kind.Number, ["targetMode"] = Kind.Text,
        ["filters"] = Kind.List, ["funnelStages"] = Kind.List, ["funnelStyle"] = Kind.Text, ["funnelMode"] = Kind.Text,
        ["cohortField"] = Kind.Text, ["config"] = Kind.Text,
    };

    private static readonly string[] ServerManaged = ["id", "tenant_id", "created_at", "created_by", "updated_at", "updated_by", "locked"];

    private static readonly string[] FunnelStageNames =
        ["label", "entityName", "entityNames", "filters", "withinDays", "minCount", "source", "sourceFilter"];

    private static readonly Dictionary<string, (string Key, string[] Allowed, string Label)> Enumerations = new(StringComparer.Ordinal)
    {
        ["chartType"] = ("--type", ChartVocabulary.ChartTypes, "chart type"),
        ["dataSource"] = ("--source", ChartVocabulary.Sources, "data source"),
        ["yAgg"] = ("--agg", ChartVocabulary.Aggregations, "aggregation"),
        ["xInterval"] = ("--interval", ChartVocabulary.Intervals, "interval"),
        ["sortDirection"] = ("--sort", ChartVocabulary.Sorts, "sort direction"),
        ["timeframePreset"] = ("--timeframe", ChartVocabulary.Timeframes, "timeframe"),
        ["targetMode"] = ("--target-mode", ChartVocabulary.TargetModes, "target mode"),
        ["platformPeriod"] = ("--platform-period", ChartVocabulary.PlatformPeriods, "platform period"),
        ["funnelStyle"] = ("funnelStyle", ChartVocabulary.FunnelStyles, "funnel style"),
        ["funnelMode"] = ("funnelMode", ChartVocabulary.FunnelModes, "funnel mode"),
    };

    public static ChartBuild Build(ChartInput input, JsonObject? existing, bool create)
    {
        var problems = new List<string>();
        var body = existing is null ? new JsonObject { ["dataSource"] = "entity" } : ExistingAsRequest(existing);

        if (!string.IsNullOrWhiteSpace(input.Config))
            ApplyConfig(body, input.Config, problems);

        ApplyFlags(body, input, problems);
        Normalise(body, problems);
        Validate(body, create, problems);

        return new ChartBuild(problems.Count == 0 ? body : null, problems);
    }

    internal static JsonObject ExistingAsRequest(JsonObject chart)
    {
        var body = new JsonObject();
        foreach (var (key, value) in chart)
        {
            if (value is null || ServerManaged.Contains(key)) continue;

            var wire = JsonNames.ToCamel(key);
            if (!Settings.ContainsKey(wire)) continue;

            body[wire] = wire is "filters" or "funnelStages" && value is JsonValue text && text.TryGetValue<string>(out var raw)
                ? ParseOrNull(raw)
                : value.DeepClone();
        }

        body["dataSource"] ??= "entity";
        return body;
    }

    private static JsonNode? ParseOrNull(string raw)
    {
        try { return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw); }
        catch (JsonException) { return null; }
    }

    private static void ApplyConfig(JsonObject body, string config, List<string> problems)
    {
        JsonObject? parsed;
        try
        {
            parsed = JsonNode.Parse(config) as JsonObject;
        }
        catch (JsonException ex)
        {
            problems.Add($"--config isn't valid JSON ({ex.Message.Split('.')[0]}). Pass an object such as '{{\"funnelStages\":[...]}}'.");
            return;
        }

        if (parsed is null)
        {
            problems.Add("--config must be a JSON object such as '{\"limit\":10}'.");
            return;
        }

        var unknown = new List<string>();
        foreach (var (key, value) in parsed)
        {
            var wire = JsonNames.ToCamel(key);
            if (!Settings.ContainsKey(wire))
            {
                unknown.Add(key);
                continue;
            }

            body[wire] = wire == "funnelStages" ? WithStageNames(value) : value?.DeepClone();
        }

        if (unknown.Count > 0)
            problems.Add($"--config has settings a chart doesn't have: {string.Join(", ", unknown)}. " +
                         $"Known settings: {string.Join(", ", Settings.Keys.Order(StringComparer.Ordinal))}.");
    }

    private static JsonNode? WithStageNames(JsonNode? stages)
    {
        if (stages is not JsonArray list) return stages?.DeepClone();

        var result = new JsonArray();
        foreach (var stage in list)
        {
            if (stage is not JsonObject item)
            {
                result.Add(stage?.DeepClone());
                continue;
            }

            var renamed = new JsonObject();
            foreach (var (key, value) in item)
            {
                var name = JsonNames.ToCamel(key);
                renamed[FunnelStageNames.Contains(name) ? name : key] = value?.DeepClone();
            }
            if (renamed["filters"] is JsonArray stageFilters) StringifyFilterValues(stageFilters);
            result.Add(renamed);
        }
        return result;
    }

    private static void ApplyFlags(JsonObject body, ChartInput input, List<string> problems)
    {
        Text(body, "name", input.Name?.Trim());
        Text(body, "chartType", input.Type);
        Text(body, "dataSource", input.Source);
        Text(body, "entityName", input.Entity);
        Text(body, "xField", input.GroupBy);
        Text(body, "xInterval", input.Interval);
        Text(body, "yField", input.Measure);
        Text(body, "yAgg", input.Agg);
        Text(body, "stackBy", input.StackBy);
        Text(body, "sortDirection", input.Sort);
        Text(body, "targetMode", input.TargetMode);
        Text(body, "platformMetric", input.PlatformMetric);
        Text(body, "platformPeriod", input.PlatformPeriod);
        Text(body, "quotaMetric", input.QuotaMetric);

        if (input.Limit is { } limit) body["limit"] = limit;
        if (input.Cumulative is { } cumulative) body["cumulative"] = cumulative;
        if (input.Target is { } target) body["target"] = target;
        if (input.Compare is { } compare) body["compareEnabled"] = compare;
        if (input.CompareDays is { } days) body["comparePreviousDays"] = days;
        if (input.CompareOverlay is { } overlay) body["compareShowOverlay"] = overlay;

        if (!string.IsNullOrWhiteSpace(input.Timeframe))
        {
            body["timeframePreset"] = input.Timeframe;
            if (!string.Equals(input.Timeframe.Trim(), "custom", StringComparison.OrdinalIgnoreCase))
            {
                body["timeframeFrom"] = null;
                body["timeframeTo"] = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(input.From) || !string.IsNullOrWhiteSpace(input.To))
        {
            if (!string.IsNullOrWhiteSpace(input.Timeframe) && !string.Equals(input.Timeframe.Trim(), "custom", StringComparison.OrdinalIgnoreCase))
                problems.Add($"--from and --to set a custom range, so they can't go with --timeframe {input.Timeframe}. Drop --timeframe, or use --timeframe custom.");
            else
                body["timeframePreset"] = "custom";

            if (!string.IsNullOrWhiteSpace(input.From)) body["timeframeFrom"] = input.From;
            if (!string.IsNullOrWhiteSpace(input.To)) body["timeframeTo"] = input.To;
        }

        if (input.Filters is { Count: > 0 } filters)
        {
            var list = new JsonArray();
            foreach (var text in filters)
            {
                var parsed = ParseFilter(text, out var error);
                if (parsed is null) problems.Add(error!);
                else list.Add(parsed);
            }
            body["filters"] = list;
        }
    }

    private static void Text(JsonObject body, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) body[key] = value.Trim();
    }

    public static JsonObject? ParseFilter(string text, out string? error)
    {
        error = null;
        var first = text.IndexOf(':');
        var second = first < 0 ? -1 : text.IndexOf(':', first + 1);
        var field = (first < 0 ? text : text[..first]).Trim();
        var op = first < 0 ? "" : (second < 0 ? text[(first + 1)..] : text[(first + 1)..second]).Trim().ToLowerInvariant();
        var value = second < 0 ? null : text[(second + 1)..];

        if (field.Length == 0 || op.Length == 0)
        {
            error = $"Filter '{text}' needs the form field:operator:value, for example status:eq:published. Operators: {ChartVocabulary.List(ChartVocabulary.FilterOperators)}.";
            return null;
        }

        var filter = new JsonObject { ["field"] = field, ["op"] = op };
        if (value is { Length: > 0 }) filter["value"] = value;
        return filter;
    }

    private static void Normalise(JsonObject body, List<string> problems)
    {
        foreach (var key in Enumerations.Keys)
        {
            if (body[key] is not JsonValue node || !node.TryGetValue<string>(out var text)) continue;
            var lower = text.Trim().ToLowerInvariant();
            body[key] = key == "chartType" && lower == "bar" ? "column" : lower;
        }

        foreach (var key in new[] { "timeframeFrom", "timeframeTo" })
        {
            if (body[key] is not JsonValue node || !node.TryGetValue<string>(out var text)) continue;
            var moment = Moment(text, end: key == "timeframeTo");
            if (moment is null)
                problems.Add($"{(key == "timeframeFrom" ? "--from" : "--to")} '{text}' isn't a date. Use 2026-01-31 or 2026-01-31T09:30:00Z.");
            else
                body[key] = moment;
        }

        if (body["filters"] is JsonArray filters) StringifyFilterValues(filters);
    }

    // The API reads a filter's value as text and rejects a bare number or boolean.
    private static void StringifyFilterValues(JsonArray filters)
    {
        foreach (var item in filters.OfType<JsonObject>())
        {
            if (item["op"] is JsonValue op && op.TryGetValue<string>(out var name)) item["op"] = name.Trim().ToLowerInvariant();
            if (item["value"] is JsonValue value && !value.TryGetValue<string>(out _))
                item["value"] = value.ToJsonString().Trim('"');
        }
    }

    private static string? Moment(string text, bool end)
    {
        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        var trimmed = text.Trim();
        if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, styles, out var day))
            return day.ToString(end ? "yyyy-MM-dd'T'23:59:59'Z'" : "yyyy-MM-dd'T'00:00:00'Z'", CultureInfo.InvariantCulture);
        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, styles, out var moment))
            return moment.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return null;
    }

    private static void Validate(JsonObject body, bool create, List<string> problems)
    {
        foreach (var (key, kind) in Settings)
        {
            if (body[key] is not { } node) continue;
            if (!Matches(node, kind))
                problems.Add($"{key} must be {Describe(kind)}.");
        }

        foreach (var (key, (flag, allowed, label)) in Enumerations)
        {
            if (body[key] is JsonValue node && node.TryGetValue<string>(out var text) && !allowed.Contains(text))
                problems.Add($"'{text}' isn't a {label} ({flag}). Use one of: {ChartVocabulary.List(allowed)}.");
        }

        if (create && Present(body, "name") is null)
            problems.Add("A chart needs a name: anythink charts create <NAME> ...");

        var source = Present(body, "dataSource") ?? "entity";
        var type = Present(body, "chartType");
        if (type is null)
            problems.Add($"A chart needs --type, one of: {ChartVocabulary.List(ChartVocabulary.ChartTypes)}.");

        switch (source)
        {
            case "entity":
                ValidateEntityChart(body, type, problems);
                break;
            case "platform" when Present(body, "platformMetric") is null:
                problems.Add("A platform chart needs --platform-metric <ID>. List the metrics with: anythink charts platform-metrics");
                break;
            case "quota" when Present(body, "quotaMetric") is null:
                problems.Add("A quota chart needs --quota-metric <ID>, one of: storage, files, users, workflows, emails.");
                break;
        }

        ValidateTimeframe(body, problems);
        ValidateFilters(body, problems);

        if (body["limit"] is JsonValue limit && limit.TryGetValue<int>(out var rows) && rows < 1)
            problems.Add("--limit must be at least 1.");
        if (body["comparePreviousDays"] is JsonValue days && days.TryGetValue<int>(out var back) && back < 1)
            problems.Add("--compare-days must be at least 1.");
    }

    private static void ValidateEntityChart(JsonObject body, string? type, List<string> problems)
    {
        if (Present(body, "entityName") is null)
            problems.Add("An entity chart needs --entity <ENTITY>, the entity whose records it plots.");

        if (type is "column" or "line" or "area" or "pie" && Present(body, "xField") is null)
            problems.Add($"A {type} chart needs --group-by <FIELD>, the field for the {(type == "pie" ? "slices" : "x axis")}.");

        var agg = Present(body, "yAgg");
        if (agg is not null and not "count" && Present(body, "yField") is null)
            problems.Add($"--agg {agg} needs --measure <FIELD>, the numeric field to {AggregationVerb(agg)}.");

        if (type == "funnel")
        {
            if (body["funnelStages"] is not JsonArray { Count: > 0 } stages)
                problems.Add("A funnel chart needs stages. Pass them in --config, for example " +
                             "--config '{\"funnelStages\":[{\"label\":\"Signed up\"},{\"label\":\"Paid\",\"filters\":[{\"field\":\"plan\",\"op\":\"ne\",\"value\":\"free\"}]}]}'.");
            else
                for (var i = 0; i < stages.Count; i++)
                    if (stages[i] is not JsonObject stage || Present(stage, "label") is null)
                        problems.Add($"Funnel stage {i + 1} needs a label.");
            if (Present(body, "funnelMode") == "cohort" && Present(body, "cohortField") is null)
                problems.Add("A cohort funnel needs a cohortField in --config, the field that identifies who moves through the stages, for example {\"cohortField\":\"user_id\"}.");
        }
    }

    private static string AggregationVerb(string agg) => agg switch
    {
        "sum" => "add up",
        "avg" => "average",
        "min" => "take the lowest of",
        "max" => "take the highest of",
        _ => "aggregate",
    };

    private static void ValidateTimeframe(JsonObject body, List<string> problems)
    {
        var preset = Present(body, "timeframePreset");
        var from = Present(body, "timeframeFrom");
        var to = Present(body, "timeframeTo");

        if (preset == "custom" && from is null && to is null)
            problems.Add("--timeframe custom needs --from and/or --to, for example --from 2026-01-01 --to 2026-03-31.");

        if (from is not null && to is not null
            && DateTime.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start)
            && DateTime.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var finish)
            && finish < start)
            problems.Add($"--to ({to}) is before --from ({from}).");
    }

    private static void ValidateFilters(JsonObject body, List<string> problems)
    {
        if (body["filters"] is not JsonArray filters) return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in filters)
        {
            if (item is not JsonObject filter)
            {
                problems.Add("Each filter must be an object with field, op and value.");
                continue;
            }

            var field = Present(filter, "field");
            var op = Present(filter, "op");
            if (field is null || op is null)
            {
                problems.Add($"A filter needs a field and an operator: {filter.ToJsonString()}.");
                continue;
            }

            if (!ChartVocabulary.FilterOperators.Contains(op))
                problems.Add($"Filter operator '{op}' on {field} isn't one of: {ChartVocabulary.List(ChartVocabulary.FilterOperators)}.");
            else if (!ChartVocabulary.WithoutValue.Contains(op) && Present(filter, "value") is null)
                problems.Add($"The {op} filter on {field} needs a value, for example {field}:{op}:10.");

            // The chart engine keeps one filter per field, so a second one would silently replace the first.
            if (!seen.Add(field))
                problems.Add($"There are two filters on {field}; a chart keeps one filter per field.");
        }
    }

    private static string? Present(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static bool Matches(JsonNode node, Kind kind) => node.GetValueKind() switch
    {
        JsonValueKind.String => kind is Kind.Text or Kind.Moment,
        JsonValueKind.Number => kind is Kind.Number || (kind is Kind.Whole && node is JsonValue n && n.TryGetValue<long>(out _)),
        JsonValueKind.True or JsonValueKind.False => kind is Kind.Flag,
        JsonValueKind.Array => kind is Kind.List,
        JsonValueKind.Null => true,
        _ => false,
    };

    private static string Describe(Kind kind) => kind switch
    {
        Kind.Text or Kind.Moment => "text",
        Kind.Whole => "a whole number",
        Kind.Number => "a number",
        Kind.Flag => "true or false",
        _ => "a list",
    };
}

public static class JsonNames
{
    public static string ToCamel(string name)
    {
        if (!name.Contains('_')) return name;

        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var text = new StringBuilder(parts[0]);
        foreach (var part in parts.Skip(1))
            text.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
        return text.ToString();
    }

    public static string Label(string snake)
    {
        var words = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? snake : string.Join(' ', words.Select((w, i) => i == 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
    }
}
