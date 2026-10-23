using ExpenseWorkflow.Api.Data;
using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Engine;

/// <summary>A user's own out-of-office windows, and keeping already-active approval tasks in step with them.</summary>
public class DelegationManager(AppDbContext db, TimeProvider clock, DelegationService resolver)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>Windows that haven't ended yet, soonest first.</summary>
    public Task<List<Delegation>> ListAsync(int userId) =>
        db.Delegations.Include(d => d.DelegateUser)
            .Where(d => d.UserId == userId && d.EndsOn >= Today)
            .OrderBy(d => d.StartsOn).ToListAsync();

    public async Task<Delegation> CreateAsync(User me, int delegateUserId, DateOnly startsOn, DateOnly endsOn)
    {
        var delegateUser = await db.Users.FindAsync(delegateUserId)
            ?? throw new EngineException(EngineError.Invalid, "The delegate doesn't exist.");
        if (delegateUser.Id == me.Id)
            throw new EngineException(EngineError.Invalid, "You can't delegate to yourself.");
        if (endsOn < startsOn)
            throw new EngineException(EngineError.Invalid, "The end date can't be before the start date.");
        if (endsOn < Today)
            throw new EngineException(EngineError.Invalid, "The end date can't be in the past.");
        if (await db.Delegations.AnyAsync(d => d.UserId == me.Id && d.StartsOn <= endsOn && d.EndsOn >= startsOn))
            throw new EngineException(EngineError.Conflict, "This overlaps one of your existing out-of-office windows.");

        var delegation = new Delegation { UserId = me.Id, DelegateUserId = delegateUser.Id, DelegateUser = delegateUser, StartsOn = startsOn, EndsOn = endsOn };
        db.Delegations.Add(delegation);
        await db.SaveChangesAsync();
        await RefreshActiveTasksAsync(me.Id);
        return delegation;
    }

    public async Task RemoveAsync(User me, int id)
    {
        var delegation = await db.Delegations.FirstOrDefaultAsync(d => d.Id == id && d.UserId == me.Id)
            ?? throw new EngineException(EngineError.NotFound, "Delegation not found.");
        db.Delegations.Remove(delegation);
        await db.SaveChangesAsync();
        await RefreshActiveTasksAsync(me.Id);
    }

    /// <summary>
    /// A window set (or cancelled) while a task is already waiting on someone must move that task too,
    /// otherwise reports would sit with an approver who is already away.
    /// </summary>
    private async Task RefreshActiveTasksAsync(int userId)
    {
        var tasks = await db.ApprovalTasks.Include(t => t.Report)
            .Where(t => t.Status == ApprovalTaskStatus.Active && t.ResolvedApproverId != null
                && (t.ResolvedApproverId == userId || t.EffectiveApproverId == userId))
            .ToListAsync();

        foreach (var task in tasks)
        {
            var resolved = task.ResolvedApproverId!.Value;
            var effective = await resolver.ResolveAsync(resolved, task.Report.SubmitterId, Today);
            if (effective == task.EffectiveApproverId) continue;

            task.EffectiveApproverId = effective;
            task.DelegatedFromId = effective != resolved ? resolved : null;
            db.AuditEvents.Add(new AuditEvent
            {
                ReportId = task.ReportId, Type = "TaskReassigned", At = clock.GetUtcNow().UtcDateTime,
                Detail = $"Step {task.Sequence} now with user {effective} (out-of-office change by user {userId})."
            });
        }
        await db.SaveChangesAsync();
    }
}
