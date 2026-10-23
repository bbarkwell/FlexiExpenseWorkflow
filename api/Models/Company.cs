namespace ExpenseWorkflow.Api.Models;

/// <summary>Single row: one company per deployment.</summary>
public class Company
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
}
