namespace ExpenseWorkflow.Api.Models;

/// <summary>Versioned; once Published it is immutable. Reports pin to the version in force at submit.</summary>
public class WorkflowDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Draft;
    public DateTime? EffectiveFrom { get; set; }
    public List<WorkflowRule> Rules { get; set; } = [];
}
