namespace ExpenseWorkflow.Api.Models;

public class WorkflowRule
{
    public int Id { get; set; }
    public int DefinitionId { get; set; }
    public WorkflowDefinition Definition { get; set; } = null!;
    /// <summary>Lowest priority number is evaluated first; first match wins.</summary>
    public int Priority { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>JSON condition tree; null means catch-all.</summary>
    public string? ConditionJson { get; set; }
    public List<WorkflowStep> Steps { get; set; } = [];
}
