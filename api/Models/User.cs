namespace ExpenseWorkflow.Api.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? ManagerId { get; set; }
    public User? Manager { get; set; }
    public List<Role> Roles { get; set; } = [];
}
