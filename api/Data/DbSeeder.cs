using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Data;

/// <summary>Sample company and org chart for development.</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Companies.AnyAsync()) return;

        db.Companies.Add(new Company { Name = "Acme Corp", Currency = "USD" });

        var employee = new Role { Name = "Employee" };
        var admin = new Role { Name = "Admin" };
        var finance = new Role { Name = "Finance" };
        db.Roles.AddRange(employee, admin, finance);

        var engineering = new Department { Name = "Engineering" };
        db.Departments.Add(engineering);

        var dana = new User { Name = "Dana Head", Email = "dana@acme.test", Department = engineering, Roles = [employee] };
        var mark = new User { Name = "Mark Manager", Email = "mark@acme.test", Department = engineering, Manager = dana, Roles = [employee] };
        var mia = new User { Name = "Mia Manager", Email = "mia@acme.test", Department = engineering, Manager = dana, Roles = [employee] };
        var eve = new User { Name = "Eve Employee", Email = "eve@acme.test", Department = engineering, Manager = mark, Roles = [employee] };
        var frank = new User { Name = "Frank Finance", Email = "frank@acme.test", Roles = [employee, finance] };
        var alice = new User { Name = "Alice Admin", Email = "alice@acme.test", Roles = [employee, admin] };
        db.Users.AddRange(dana, mark, mia, eve, frank, alice);
        await db.SaveChangesAsync();

        // Department head is set after users exist: Department and User reference each other.
        engineering.HeadUser = dana;

        // Mark is out of office for the next two weeks; Mia covers.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.Delegations.Add(new Delegation { User = mark, DelegateUser = mia, StartsOn = today, EndsOn = today.AddDays(14) });

        // Amounts are in cents. Boundaries: $500.00 and $5,000.00 both fall in the middle band.
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Name = "Expense approval",
            Version = 1,
            Status = WorkflowStatus.Published,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
            Rules =
            [
                new WorkflowRule
                {
                    Priority = 1, Name = "Under $500",
                    ConditionJson = """{ "field": "totalCents", "op": "lt", "value": 50000 }""",
                    Steps = [new WorkflowStep { Sequence = 1, ApproverType = ApproverType.Manager }]
                },
                new WorkflowRule
                {
                    Priority = 2, Name = "$500 to $5,000",
                    ConditionJson = """
                        { "all": [ { "field": "totalCents", "op": "gte", "value": 50000 },
                                   { "field": "totalCents", "op": "lte", "value": 500000 } ] }
                        """,
                    Steps =
                    [
                        new WorkflowStep { Sequence = 1, ApproverType = ApproverType.Manager },
                        new WorkflowStep { Sequence = 2, ApproverType = ApproverType.DepartmentHead }
                    ]
                },
                new WorkflowRule
                {
                    Priority = 3, Name = "Over $5,000",
                    ConditionJson = """{ "field": "totalCents", "op": "gt", "value": 500000 }""",
                    Steps =
                    [
                        new WorkflowStep { Sequence = 1, ApproverType = ApproverType.Manager },
                        new WorkflowStep { Sequence = 2, ApproverType = ApproverType.DepartmentHead },
                        new WorkflowStep { Sequence = 3, ApproverType = ApproverType.Role, ApproverParam = "Finance" }
                    ]
                }
            ]
        });

        await db.SaveChangesAsync();
    }
}
