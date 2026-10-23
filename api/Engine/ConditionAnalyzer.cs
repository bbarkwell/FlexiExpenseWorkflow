using System.Text.Json;

namespace ExpenseWorkflow.Api.Engine;

public record ConditionAnalysis(List<string> Errors, HashSet<string> Fields, List<long> TotalConstants);

/// <summary>
/// Walks an entire condition tree without evaluating it, so a draft can be checked before publish:
/// reports every structural problem (not just the first) and collects which fields and amounts it uses.
/// </summary>
public static class ConditionAnalyzer
{
    private static readonly HashSet<string> NumericFields = ["totalCents", "lineCount"];
    private static readonly HashSet<string> TextFields = ["department", "submitterRole", "category"];
    private static readonly HashSet<string> NumericOps = ["lt", "lte", "eq", "neq", "gte", "gt", "in"];
    private static readonly HashSet<string> TextOps = ["eq", "neq", "in"];

    public static ConditionAnalysis Analyze(string? json)
    {
        var result = new ConditionAnalysis([], [], []);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, result);
        }
        catch (JsonException e)
        {
            result.Errors.Add($"Not valid JSON: {e.Message}");
        }
        return result;
    }

    private static void Walk(JsonElement node, ConditionAnalysis r)
    {
        if (node.ValueKind != JsonValueKind.Object) { r.Errors.Add("Condition nodes must be objects."); return; }

        foreach (var key in new[] { "all", "any" })
        {
            if (!node.TryGetProperty(key, out var children)) continue;
            if (children.ValueKind != JsonValueKind.Array || children.GetArrayLength() == 0)
                r.Errors.Add($"'{key}' needs a non-empty array.");
            else
                foreach (var child in children.EnumerateArray()) Walk(child, r);
            return;
        }

        if (!node.TryGetProperty("field", out var fieldEl) || !node.TryGetProperty("op", out var opEl) || !node.TryGetProperty("value", out var value))
        {
            r.Errors.Add("A comparison needs 'field', 'op' and 'value'.");
            return;
        }

        var field = fieldEl.ValueKind == JsonValueKind.String ? fieldEl.GetString()! : "";
        var op = opEl.ValueKind == JsonValueKind.String ? opEl.GetString()! : "";
        var numeric = NumericFields.Contains(field);
        if (!numeric && !TextFields.Contains(field)) { r.Errors.Add($"Unknown field '{field}'."); return; }
        r.Fields.Add(field);

        if (!(numeric ? NumericOps : TextOps).Contains(op)) { r.Errors.Add($"Operator '{op}' isn't valid for '{field}'."); return; }

        var values = op == "in"
            ? (value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : null)
            : [value];
        if (values is null || values.Count == 0) { r.Errors.Add($"'in' on '{field}' needs a non-empty array."); return; }

        foreach (var v in values)
        {
            if (numeric)
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) { if (field == "totalCents") r.TotalConstants.Add(n); }
                else r.Errors.Add($"'{field}' needs whole-number values.");
            }
            else if (v.ValueKind != JsonValueKind.String)
            {
                r.Errors.Add($"'{field}' needs text values.");
            }
        }
    }
}
