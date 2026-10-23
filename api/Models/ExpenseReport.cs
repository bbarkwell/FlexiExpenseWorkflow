namespace ExpenseWorkflow.Api.Models;

public class ExpenseReport
{
    public int Id { get; set; }
    public int SubmitterId { get; set; }
    public User Submitter { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    /// <summary>Money is stored as integer minor units (cents).</summary>
    public long TotalCents { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Draft;
    public int? WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }
    public int? MatchedRuleId { get; set; }
    public WorkflowRule? MatchedRule { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public List<ExpenseLine> Lines { get; set; } = [];
    public List<ApprovalTask> Tasks { get; set; } = [];
}
