using ExpenseWorkflow.Api.Data;
using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Engine;

/// <summary>Who a step points at: a specific user, or any holder of a role.</summary>
public record Assignee(int? UserId, int? RoleId);

public interface IApproverResolver
{
    ApproverType Type { get; }

    /// <summary>Throws EngineException(Invalid) when the org chart can't answer.</summary>
    Task<Assignee> ResolveAsync(WorkflowStep step, User submitter);
}

public class ManagerResolver : IApproverResolver
{
    public ApproverType Type => ApproverType.Manager;

    public Task<Assignee> ResolveAsync(WorkflowStep step, User submitter) =>
        submitter.ManagerId is { } id
            ? Task.FromResult(new Assignee(id, null))
            : throw new EngineException(EngineError.Invalid, $"{submitter.Name} has no manager, so the Manager step can't be resolved.");
}

public class DepartmentHeadResolver(AppDbContext db) : IApproverResolver
{
    public ApproverType Type => ApproverType.DepartmentHead;

    public async Task<Assignee> ResolveAsync(WorkflowStep step, User submitter)
    {
        var dept = submitter.DepartmentId is { } id ? await db.Departments.FindAsync(id) : null;
        return dept?.HeadUserId is { } head
            ? new Assignee(head, null)
            : throw new EngineException(EngineError.Invalid, $"{submitter.Name}'s department has no head, so the Department Head step can't be resolved.");
    }
}

public class SpecificUserResolver(AppDbContext db) : IApproverResolver
{
    public ApproverType Type => ApproverType.SpecificUser;

    public async Task<Assignee> ResolveAsync(WorkflowStep step, User submitter)
    {
        if (!int.TryParse(step.ApproverParam, out var id) || await db.Users.FindAsync(id) is null)
            throw new EngineException(EngineError.Invalid, $"Step {step.Sequence} names a user that doesn't exist.");
        return new Assignee(id, null);
    }
}

public class RoleResolver(AppDbContext db) : IApproverResolver
{
    public ApproverType Type => ApproverType.Role;

    public async Task<Assignee> ResolveAsync(WorkflowStep step, User submitter)
    {
        var role = await db.Roles.Include(r => r.Users).FirstOrDefaultAsync(r => r.Name == step.ApproverParam)
            ?? throw new EngineException(EngineError.Invalid, $"Step {step.Sequence} names the role '{step.ApproverParam}', which doesn't exist.");
        // The submitter can never approve their own report, so someone else must hold the role.
        if (!role.Users.Any(u => u.Id != submitter.Id))
            throw new EngineException(EngineError.Invalid, $"No one other than the submitter holds the role '{role.Name}'.");
        return new Assignee(null, role.Id);
    }
}
