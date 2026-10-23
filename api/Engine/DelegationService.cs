using ExpenseWorkflow.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Engine;

public class DelegationService(AppDbContext db)
{
    private const int MaxHops = 5;

    /// <summary>
    /// Returns who should act for <paramref name="approverId"/> on <paramref name="today"/>.
    /// Follows delegate chains; if the chain loops, runs too long, or lands on the submitter,
    /// falls back to the original approver's manager (or the approver themself if none).
    /// </summary>
    public async Task<int> ResolveAsync(int approverId, int submitterId, DateOnly today)
    {
        var seen = new HashSet<int> { approverId };
        var current = approverId;

        for (var hop = 0; hop < MaxHops; hop++)
        {
            var from = current;
            var delegation = await db.Delegations
                .Where(d => d.UserId == from && d.StartsOn <= today && d.EndsOn >= today)
                .OrderByDescending(d => d.StartsOn)
                .FirstOrDefaultAsync();

            if (delegation is null)
                return current == submitterId ? await FallbackAsync(approverId, submitterId) : current;
            if (!seen.Add(delegation.DelegateUserId))
                return await FallbackAsync(approverId, submitterId);
            current = delegation.DelegateUserId;
        }
        return await FallbackAsync(approverId, submitterId);
    }

    private async Task<int> FallbackAsync(int approverId, int submitterId)
    {
        var manager = (await db.Users.FindAsync(approverId))?.ManagerId;
        return manager is { } m && m != submitterId ? m : approverId;
    }
}
