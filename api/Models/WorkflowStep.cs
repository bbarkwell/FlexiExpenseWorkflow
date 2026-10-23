namespace ExpenseWorkflow.Api.Models;

public class WorkflowStep
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public WorkflowRule Rule { get; set; } = null!;
    public int Sequence { get; set; }
    public ApproverType ApproverType { get; set; }
    /// <summary>Role name for Role, user id for SpecificUser; unused otherwise.</summary>
    public string? ApproverParam { get; set; }
}
