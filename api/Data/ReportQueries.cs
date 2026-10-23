using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Data;

public static class ReportQueries
{
    /// <summary>Everything ReportDto needs: submitter, lines and the approval timeline.</summary>
    public static IQueryable<ExpenseReport> WithDetails(this IQueryable<ExpenseReport> q) => q
        .Include(r => r.Submitter)
        .Include(r => r.Lines)
        .Include(r => r.Tasks).ThenInclude(t => t.ResolvedApprover)
        .Include(r => r.Tasks).ThenInclude(t => t.EffectiveApprover)
        .Include(r => r.Tasks).ThenInclude(t => t.DelegatedFrom)
        .Include(r => r.Tasks).ThenInclude(t => t.DecidedBy)
        .Include(r => r.Tasks).ThenInclude(t => t.AssignedRole)
        .AsSplitQuery();
}
