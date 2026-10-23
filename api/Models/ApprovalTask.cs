namespace ExpenseWorkflow.Api.Models;

public class ApprovalTask
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public ExpenseReport Report { get; set; } = null!;
    public int Sequence { get; set; }
    public int? StepId { get; set; }
    public WorkflowStep? Step { get; set; }
    /// <summary>Who the step resolved to from the org chart. Null for role steps.</summary>
    public int? ResolvedApproverId { get; set; }
    public User? ResolvedApprover { get; set; }
    /// <summary>Who actually decides (a delegate, when the resolved approver is out of office). Null for role steps.</summary>
    public int? EffectiveApproverId { get; set; }
    public User? EffectiveApprover { get; set; }
    /// <summary>For role steps: any one holder of this role may decide.</summary>
    public int? AssignedRoleId { get; set; }
    public Role? AssignedRole { get; set; }
    /// <summary>Who actually made the decision.</summary>
    public int? DecidedById { get; set; }
    public User? DecidedBy { get; set; }
    public int? DelegatedFromId { get; set; }
    public User? DelegatedFrom { get; set; }
    public ApprovalTaskStatus Status { get; set; } = ApprovalTaskStatus.Pending;
    public DateTime? DecidedAt { get; set; }
    public string? Comment { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
