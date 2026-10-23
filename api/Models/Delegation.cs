namespace ExpenseWorkflow.Api.Models;

/// <summary>Out-of-office window: while active, approvals for UserId go to DelegateUserId.</summary>
public class Delegation
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int DelegateUserId { get; set; }
    public User DelegateUser { get; set; } = null!;
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
}
