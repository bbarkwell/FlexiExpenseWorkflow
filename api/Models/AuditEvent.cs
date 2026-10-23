namespace ExpenseWorkflow.Api.Models;

public class AuditEvent
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public ExpenseReport Report { get; set; } = null!;
    public int? ActorId { get; set; }
    public User? Actor { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}
