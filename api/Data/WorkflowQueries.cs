using ExpenseWorkflow.Api.Models;

namespace ExpenseWorkflow.Api.Data;

public static class WorkflowQueries
{
    /// <summary>
    /// Published definitions already in effect, newest first. The first one is "current":
    /// later versions supersede earlier ones, and a future EffectiveFrom schedules a cutover.
    /// </summary>
    public static IQueryable<WorkflowDefinition> CurrentPublished(this IQueryable<WorkflowDefinition> q, DateTime now) => q
        .Where(d => d.Status == WorkflowStatus.Published && (d.EffectiveFrom == null || d.EffectiveFrom <= now))
        .OrderByDescending(d => d.EffectiveFrom)
        .ThenByDescending(d => d.Version);
}
