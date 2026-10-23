using ExpenseWorkflow.Api.Data;
using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Engine;

public record PlannedStep(WorkflowStep Step, Assignee Assignee, string? SkipReason);

public record WorkflowPlan(WorkflowDefinition Definition, WorkflowRule Rule, List<PlannedStep> Steps);

public class WorkflowEngine(
    AppDbContext db,
    TimeProvider clock,
    IEnumerable<IApproverResolver> resolvers,
    DelegationService delegation)
{
    public Task<User> LoadSubmitterAsync(int userId) =>
        db.Users.Include(u => u.Roles).Include(u => u.Department).FirstAsync(u => u.Id == userId);

    /// <summary>
    /// Picks the first matching rule of <paramref name="definition"/> and resolves who each step points at.
    /// Persists nothing, so it also powers the admin "simulate" call. The submitter needs Roles and Department loaded.
    /// </summary>
    public async Task<WorkflowPlan> PlanAsync(
        WorkflowDefinition definition, User submitter, long totalCents, IReadOnlyCollection<string> categories, int lineCount)
    {
        var context = new ConditionContext(
            totalCents, lineCount, submitter.Department?.Name,
            submitter.Roles.Select(r => r.Name).ToList(), categories);

        var rule = definition.Rules.OrderBy(r => r.Priority).FirstOrDefault(r => Matches(r, context))
            ?? throw new EngineException(EngineError.Invalid, "No workflow rule matches this report.");

        var steps = new List<PlannedStep>();
        int? previousApprover = null;
        foreach (var step in rule.Steps.OrderBy(s => s.Sequence))
        {
            var resolver = resolvers.FirstOrDefault(r => r.Type == step.ApproverType)
                ?? throw new EngineException(EngineError.Invalid, $"No resolver for approver type {step.ApproverType}.");
            var assignee = await resolver.ResolveAsync(step, submitter);

            string? skip = null;
            if (assignee.UserId == submitter.Id)
                skip = "the submitter is the approver";
            else if (assignee.UserId is not null && assignee.UserId == previousApprover)
                skip = "same approver as the previous step";
            else
                previousApprover = assignee.UserId;

            steps.Add(new PlannedStep(step, assignee, skip));
        }
        return new WorkflowPlan(definition, rule, steps);
    }

    /// <summary>
    /// Matches the report to a rule in the current published workflow, plans the approval chain
    /// and activates the first step. The report must be a Draft with its lines loaded.
    /// </summary>
    public async Task SubmitAsync(ExpenseReport report)
    {
        if (report.Status != ReportStatus.Draft)
            throw new EngineException(EngineError.Conflict, "Only draft reports can be submitted.");
        if (report.Lines.Count == 0 || report.TotalCents <= 0)
            throw new EngineException(EngineError.Invalid, "Add at least one expense line before submitting.");

        var now = clock.GetUtcNow().UtcDateTime;
        var definition = await db.WorkflowDefinitions
            .Include(d => d.Rules).ThenInclude(r => r.Steps)
            .CurrentPublished(now)
            .FirstOrDefaultAsync()
            ?? throw new EngineException(EngineError.Invalid, "No workflow is published.");

        var submitter = await LoadSubmitterAsync(report.SubmitterId);
        var plan = await PlanAsync(definition, submitter, report.TotalCents,
            report.Lines.Select(l => l.Category).Distinct().ToList(), report.Lines.Count);

        report.WorkflowDefinitionId = definition.Id;
        report.MatchedRuleId = plan.Rule.Id;
        report.Status = ReportStatus.InReview;
        report.SubmittedAt = now;
        Audit(report, submitter.Id, "Submitted", $"Rule '{plan.Rule.Name}' (workflow '{definition.Name}' v{definition.Version})");

        foreach (var planned in plan.Steps)
        {
            report.Tasks.Add(new ApprovalTask
            {
                Step = planned.Step,
                Sequence = planned.Step.Sequence,
                ResolvedApproverId = planned.Assignee.UserId,
                EffectiveApproverId = planned.Assignee.UserId,
                AssignedRoleId = planned.Assignee.RoleId,
                Status = planned.SkipReason is null ? ApprovalTaskStatus.Pending : ApprovalTaskStatus.Skipped,
            });
            if (planned.SkipReason is not null)
                Audit(report, null, "StepSkipped", $"Step {planned.Step.Sequence}: {planned.SkipReason}.");
        }

        await ActivateNextAsync(report);
        await SaveAsync();
    }

    /// <summary>Records the actor's decision on an active task and moves the report along.</summary>
    public async Task<ExpenseReport> DecideAsync(int taskId, User actor, bool approve, string? comment)
    {
        var task = await db.ApprovalTasks
            .Include(t => t.Report).ThenInclude(r => r.Tasks)
            .FirstOrDefaultAsync(t => t.Id == taskId)
            ?? throw new EngineException(EngineError.NotFound, "Approval task not found.");
        var report = task.Report;

        if (task.Status != ApprovalTaskStatus.Active)
            throw new EngineException(EngineError.Conflict, "This task is not awaiting a decision.");
        if (report.SubmitterId == actor.Id)
            throw new EngineException(EngineError.Forbidden, "You can't decide on your own report.");
        var allowed = task.EffectiveApproverId == actor.Id
            || (task.AssignedRoleId is { } roleId && actor.Roles.Any(r => r.Id == roleId));
        if (!allowed)
            throw new EngineException(EngineError.Forbidden, "This task isn't assigned to you.");
        if (!approve && string.IsNullOrWhiteSpace(comment))
            throw new EngineException(EngineError.Invalid, "A comment is required when rejecting.");

        task.Status = approve ? ApprovalTaskStatus.Approved : ApprovalTaskStatus.Rejected;
        task.DecidedAt = clock.GetUtcNow().UtcDateTime;
        task.DecidedById = actor.Id;
        task.Comment = comment;
        Audit(report, actor.Id, approve ? "Approved" : "Rejected", $"Step {task.Sequence}. {comment}".Trim());

        if (approve)
        {
            await ActivateNextAsync(report);
        }
        else
        {
            report.Status = ReportStatus.Rejected;
            SkipOpenTasks(report);
            Audit(report, actor.Id, "ReportRejected", null);
        }

        await SaveAsync();
        return report;
    }

    /// <summary>Submitter pulls back a report that is still in review.</summary>
    public async Task WithdrawAsync(ExpenseReport report, User actor)
    {
        if (report.SubmitterId != actor.Id)
            throw new EngineException(EngineError.Forbidden, "Only the submitter can withdraw a report.");
        if (report.Status != ReportStatus.InReview)
            throw new EngineException(EngineError.Conflict, "Only reports in review can be withdrawn.");

        report.Status = ReportStatus.Withdrawn;
        SkipOpenTasks(report);
        Audit(report, actor.Id, "Withdrawn", null);
        await SaveAsync();
    }

    private static bool Matches(WorkflowRule rule, ConditionContext context)
    {
        try { return ConditionEvaluator.Evaluate(rule.ConditionJson, context); }
        catch (InvalidConditionException e)
        {
            throw new EngineException(EngineError.Invalid, $"Rule '{rule.Name}' has an invalid condition: {e.Message}");
        }
    }

    /// <summary>Activates the next pending task (resolving delegation as of today), or completes the report.</summary>
    private async Task ActivateNextAsync(ExpenseReport report)
    {
        var next = report.Tasks.Where(t => t.Status == ApprovalTaskStatus.Pending).OrderBy(t => t.Sequence).FirstOrDefault();
        if (next is null)
        {
            report.Status = ReportStatus.Approved;
            Audit(report, null, "ReportApproved", null);
            return;
        }

        next.Status = ApprovalTaskStatus.Active;
        var detail = $"Step {next.Sequence}";
        if (next.ResolvedApproverId is { } resolved)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var effective = await delegation.ResolveAsync(resolved, report.SubmitterId, today);
            if (effective != resolved)
            {
                next.EffectiveApproverId = effective;
                next.DelegatedFromId = resolved;
                detail += $" delegated from user {resolved} to user {effective}";
            }
        }
        Audit(report, null, "StepActivated", detail);
    }

    private static void SkipOpenTasks(ExpenseReport report)
    {
        foreach (var t in report.Tasks.Where(t => t.Status is ApprovalTaskStatus.Pending or ApprovalTaskStatus.Active))
            t.Status = ApprovalTaskStatus.Skipped;
    }

    private void Audit(ExpenseReport report, int? actorId, string type, string? detail) =>
        db.AuditEvents.Add(new AuditEvent { Report = report, ActorId = actorId, Type = type, Detail = detail, At = clock.GetUtcNow().UtcDateTime });

    private async Task SaveAsync()
    {
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            throw new EngineException(EngineError.Conflict, "This report was changed by someone else. Reload and try again.");
        }
    }
}
