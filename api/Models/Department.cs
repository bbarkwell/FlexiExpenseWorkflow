namespace ExpenseWorkflow.Api.Models;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? HeadUserId { get; set; }
    public User? HeadUser { get; set; }
}
