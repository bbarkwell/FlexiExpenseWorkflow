using ExpenseWorkflow.Api.Engine;

namespace ExpenseWorkflow.Tests;

public class ConditionEvaluatorTests
{
    private static readonly ConditionContext Ctx = new(
        TotalCents: 100_000, LineCount: 3, Department: "Engineering",
        SubmitterRoles: ["Employee", "Finance"], Categories: ["Travel", "Meals"]);

    [Theory]
    [InlineData("""{ "field": "totalCents", "op": "lt", "value": 100000 }""", false)]
    [InlineData("""{ "field": "totalCents", "op": "lte", "value": 100000 }""", true)]
    [InlineData("""{ "field": "totalCents", "op": "gte", "value": 100000 }""", true)]
    [InlineData("""{ "field": "totalCents", "op": "gt", "value": 100000 }""", false)]
    [InlineData("""{ "field": "totalCents", "op": "eq", "value": 100000 }""", true)]
    [InlineData("""{ "field": "totalCents", "op": "neq", "value": 100000 }""", false)]
    [InlineData("""{ "field": "lineCount", "op": "in", "value": [1, 3] }""", true)]
    [InlineData("""{ "field": "department", "op": "eq", "value": "engineering" }""", true)]
    [InlineData("""{ "field": "department", "op": "neq", "value": "Sales" }""", true)]
    [InlineData("""{ "field": "category", "op": "eq", "value": "Meals" }""", true)]
    [InlineData("""{ "field": "category", "op": "neq", "value": "Meals" }""", false)]
    [InlineData("""{ "field": "submitterRole", "op": "in", "value": ["Admin", "Finance"] }""", true)]
    public void Comparisons(string json, bool expected) =>
        Assert.Equal(expected, ConditionEvaluator.Evaluate(json, Ctx));

    [Fact]
    public void NullOrBlankIsCatchAll()
    {
        Assert.True(ConditionEvaluator.Evaluate(null, Ctx));
        Assert.True(ConditionEvaluator.Evaluate("  ", Ctx));
    }

    [Fact]
    public void AllAndAnyNest()
    {
        const string json = """
            { "all": [ { "field": "totalCents", "op": "gte", "value": 50000 },
                       { "any": [ { "field": "department", "op": "eq", "value": "Sales" },
                                  { "field": "category", "op": "eq", "value": "Travel" } ] } ] }
            """;
        Assert.True(ConditionEvaluator.Evaluate(json, Ctx));
        Assert.False(ConditionEvaluator.Evaluate(json, Ctx with { Categories = ["Meals"] }));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "field": "nope", "op": "eq", "value": 1 }""")]
    [InlineData("""{ "field": "totalCents", "op": "contains", "value": 1 }""")]
    [InlineData("""{ "field": "totalCents", "op": "eq", "value": "abc" }""")]
    [InlineData("""{ "field": "totalCents", "op": "eq" }""")]
    [InlineData("""{ "all": [] }""")]
    public void MalformedConditionsThrow(string json) =>
        Assert.Throws<InvalidConditionException>(() => ConditionEvaluator.Evaluate(json, Ctx));
}
