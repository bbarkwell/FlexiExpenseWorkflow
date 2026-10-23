namespace ExpenseWorkflow.Api.Models;

public class ExpenseLine
{
    public int Id { get; set; }
    public int ReportId { get; set; }
    public ExpenseReport Report { get; set; } = null!;
    public DateOnly Date { get; set; }
    public string Category { get; set; } = string.Empty;
    public long AmountCents { get; set; }
    public string Description { get; set; } = string.Empty;
}
