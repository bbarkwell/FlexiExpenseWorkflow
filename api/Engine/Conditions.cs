using System.Text.Json;

namespace ExpenseWorkflow.Api.Engine;

public record ConditionContext(
    long TotalCents,
    int LineCount,
    string? Department,
    IReadOnlyCollection<string> SubmitterRoles,
    IReadOnlyCollection<string> Categories);

public class InvalidConditionException(string message) : Exception(message);

/// <summary>
/// Evaluates a JSON condition tree. Shapes:
///   { "all": [ ... ] }  { "any": [ ... ] }  { "field": "totalCents", "op": "gte", "value": 50000 }
/// Fields: totalCents, lineCount (numeric: lt lte eq neq gte gt in);
///         department, submitterRole, category (text: eq neq in; multi-valued fields match if any value matches).
/// A null or blank condition is a catch-all.
/// </summary>
public static class ConditionEvaluator
{
    public static bool Evaluate(string? json, ConditionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Eval(doc.RootElement, ctx);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidConditionException(e.Message);
        }
    }

    private static bool Eval(JsonElement node, ConditionContext ctx)
    {
        if (node.ValueKind != JsonValueKind.Object) throw new InvalidConditionException("Condition nodes must be objects.");

        if (node.TryGetProperty("all", out var all)) return Children(all).All(c => Eval(c, ctx));
        if (node.TryGetProperty("any", out var any)) return Children(any).Any(c => Eval(c, ctx));

        if (!node.TryGetProperty("field", out var field) || !node.TryGetProperty("op", out var op) || !node.TryGetProperty("value", out var value))
            throw new InvalidConditionException("A comparison needs 'field', 'op' and 'value'.");

        var opName = op.GetString() ?? "";
        return field.GetString() switch
        {
            "totalCents" => CompareNumber(ctx.TotalCents, opName, value),
            "lineCount" => CompareNumber(ctx.LineCount, opName, value),
            "department" => CompareText(ctx.Department is null ? [] : [ctx.Department], opName, value),
            "submitterRole" => CompareText(ctx.SubmitterRoles, opName, value),
            "category" => CompareText(ctx.Categories, opName, value),
            var f => throw new InvalidConditionException($"Unknown field '{f}'.")
        };
    }

    private static List<JsonElement> Children(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
            throw new InvalidConditionException("'all' and 'any' need a non-empty array.");
        return array.EnumerateArray().ToList();
    }

    private static bool CompareNumber(long actual, string op, JsonElement value)
    {
        if (op == "in")
            return value.EnumerateArray().Any(v => v.GetInt64() == actual);
        var target = value.GetInt64();
        return op switch
        {
            "lt" => actual < target,
            "lte" => actual <= target,
            "eq" => actual == target,
            "neq" => actual != target,
            "gte" => actual >= target,
            "gt" => actual > target,
            _ => throw new InvalidConditionException($"Unknown operator '{op}' for a numeric field.")
        };
    }

    private static bool CompareText(IReadOnlyCollection<string> actual, string op, JsonElement value)
    {
        var targets = op == "in"
            ? value.EnumerateArray().Select(v => v.GetString() ?? "").ToList()
            : [value.GetString() ?? ""];
        var anyMatch = actual.Any(a => targets.Contains(a, StringComparer.OrdinalIgnoreCase));
        return op switch
        {
            "eq" or "in" => anyMatch,
            "neq" => !anyMatch,
            _ => throw new InvalidConditionException($"Unknown operator '{op}' for a text field.")
        };
    }
}
