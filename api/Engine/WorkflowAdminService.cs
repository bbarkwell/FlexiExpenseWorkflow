using ExpenseWorkflow.Api.Data;
using ExpenseWorkflow.Api.Dtos;
using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ExpenseWorkflow.Api.Engine;

public class ValidationResult
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
}

public class WorkflowValidationException(ValidationResult result) : Exception("The workflow has validation errors.")
{
    public ValidationResult Result { get; } = result;
}

/// <summary>Drafting, validating, publishing and simulating workflows. Published versions are immutable.</summary>
public class WorkflowAdminService(AppDbContext db, TimeProvider clock, WorkflowEngine engine, DelegationService delegation)
{
    private IQueryable<WorkflowDefinition> Full => db.WorkflowDefinitions.Include(d => d.Rules).ThenInclude(r => r.Steps);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<List<WorkflowDefinition>> ListAsync() =>
        Full.OrderBy(d => d.Name).ThenByDescending(d => d.Version).ToListAsync();

    public async Task<int?> CurrentIdAsync() =>
        (await db.WorkflowDefinitions.CurrentPublished(Now).FirstOrDefaultAsync())?.Id;

    public async Task<WorkflowDefinition> GetAsync(int id) =>
        await Full.FirstOrDefaultAsync(d => d.Id == id)
            ?? throw new EngineException(EngineError.NotFound, "Workflow not found.");

    public async Task<WorkflowDefinition> CreateDraftAsync(string name, List<RuleRequest>? rules)
    {
        name = name.Trim();
        await EnsureNoDraftAsync(name);
        var version = (await db.WorkflowDefinitions.Where(d => d.Name == name).MaxAsync(d => (int?)d.Version) ?? 0) + 1;
        var draft = new WorkflowDefinition { Name = name, Version = version, Status = WorkflowStatus.Draft };
        SetRules(draft, rules);
        db.WorkflowDefinitions.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    /// <summary>Copies any version into a new draft with the next version number.</summary>
    public async Task<WorkflowDefinition> NewVersionAsync(int id)
    {
        var source = await GetAsync(id);
        await EnsureNoDraftAsync(source.Name);
        var version = await db.WorkflowDefinitions.Where(d => d.Name == source.Name).MaxAsync(d => d.Version) + 1;
        var draft = new WorkflowDefinition
        {
            Name = source.Name, Version = version, Status = WorkflowStatus.Draft,
            Rules = source.Rules.OrderBy(r => r.Priority).Select(r => new WorkflowRule
            {
                Priority = r.Priority, Name = r.Name, ConditionJson = r.ConditionJson,
                Steps = r.Steps.OrderBy(s => s.Sequence).Select(s => new WorkflowStep
                {
                    Sequence = s.Sequence, ApproverType = s.ApproverType, ApproverParam = s.ApproverParam
                }).ToList()
            }).ToList()
        };
        db.WorkflowDefinitions.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    /// <summary>Replaces all rules of a draft. Order in the list becomes evaluation priority.</summary>
    public async Task<WorkflowDefinition> SaveRulesAsync(int id, List<RuleRequest>? rules)
    {
        var draft = await GetAsync(id);
        RequireDraft(draft, "edited");
        db.WorkflowRules.RemoveRange(draft.Rules);
        draft.Rules.Clear();
        SetRules(draft, rules);
        await db.SaveChangesAsync();
        return draft;
    }

    public async Task DeleteDraftAsync(int id)
    {
        var draft = await GetAsync(id);
        RequireDraft(draft, "deleted");
        db.WorkflowDefinitions.Remove(draft);
        await db.SaveChangesAsync();
    }

    public async Task<ValidationResult> ValidateAsync(int id) => await ValidateAsync(await GetAsync(id));

    public async Task<WorkflowDefinition> PublishAsync(int id, DateTime? effectiveFrom)
    {
        var draft = await GetAsync(id);
        RequireDraft(draft, "published");
        var result = await ValidateAsync(draft);

        var from = effectiveFrom?.ToUniversalTime() ?? Now;
        if (effectiveFrom is not null && from < Now.AddMinutes(-1))
            result.Errors.Add("The effective date can't be in the past.");
        if (await db.WorkflowDefinitions.AnyAsync(d => d.Status == WorkflowStatus.Published && d.Id != draft.Id && d.EffectiveFrom > from))
            result.Errors.Add("A later version is already scheduled to take effect after this date, so this one would never be used.");
        if (result.Errors.Count > 0) throw new WorkflowValidationException(result);

        draft.Status = WorkflowStatus.Published;
        draft.EffectiveFrom = from;
        await db.SaveChangesAsync();
        return draft;
    }

    /// <summary>Dry run: which rule and which approvers a report like this would get. Works on drafts. Persists nothing.</summary>
    public async Task<SimulationDto> SimulateAsync(int id, SimulateRequest request)
    {
        var definition = await GetAsync(id);
        var submitter = await db.Users.Include(u => u.Roles).Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == request.SubmitterId)
            ?? throw new EngineException(EngineError.Invalid, "The submitter doesn't exist.");

        var categories = request.Categories ?? [];
        var plan = await engine.PlanAsync(definition, submitter, request.TotalCents, categories, request.LineCount ?? Math.Max(1, categories.Count));
        var today = DateOnly.FromDateTime(Now);

        var steps = new List<SimulatedStepDto>();
        foreach (var p in plan.Steps)
        {
            List<string> approvers;
            string? acting = null;
            if (p.Assignee.UserId is { } uid)
            {
                approvers = [(await db.Users.FindAsync(uid))!.Name];
                var effective = await delegation.ResolveAsync(uid, submitter.Id, today);
                if (effective != uid) acting = (await db.Users.FindAsync(effective))!.Name;
            }
            else
            {
                var role = await db.Roles.Include(r => r.Users).FirstAsync(r => r.Id == p.Assignee.RoleId);
                approvers = role.Users.Where(u => u.Id != submitter.Id).Select(u => u.Name).OrderBy(n => n).ToList();
            }
            steps.Add(new SimulatedStepDto(p.Step.Sequence, p.Step.ApproverType, approvers, p.SkipReason is not null, p.SkipReason, acting));
        }
        return new SimulationDto(plan.Rule.Name, plan.Rule.Priority, steps);
    }

    // ---- validation ----

    private async Task<ValidationResult> ValidateAsync(WorkflowDefinition d)
    {
        var result = new ValidationResult();
        var rules = d.Rules.OrderBy(r => r.Priority).ToList();
        if (rules.Count == 0) { result.Errors.Add("Add at least one rule."); return result; }

        var fields = new HashSet<string>();
        var constants = new List<long>();
        var roleNames = (await db.Roles.Select(r => r.Name).ToListAsync()).ToHashSet();

        foreach (var rule in rules)
        {
            var analysis = ConditionAnalyzer.Analyze(rule.ConditionJson);
            result.Errors.AddRange(analysis.Errors.Select(e => $"Rule '{rule.Name}': {e}"));
            fields.UnionWith(analysis.Fields);
            constants.AddRange(analysis.TotalConstants);

            if (rule.Steps.Count == 0) result.Errors.Add($"Rule '{rule.Name}' has no approval steps.");
            foreach (var step in rule.Steps.OrderBy(s => s.Sequence))
            {
                if (step.ApproverType == ApproverType.Role && !roleNames.Contains(step.ApproverParam ?? ""))
                    result.Errors.Add($"Rule '{rule.Name}' step {step.Sequence}: the role '{step.ApproverParam}' doesn't exist.");
                if (step.ApproverType == ApproverType.SpecificUser
                    && !(int.TryParse(step.ApproverParam, out var uid) && await db.Users.AnyAsync(u => u.Id == uid)))
                    result.Errors.Add($"Rule '{rule.Name}' step {step.Sequence}: the named user doesn't exist.");
            }
        }

        var catchAllIndex = rules.FindIndex(r => string.IsNullOrWhiteSpace(r.ConditionJson));
        if (catchAllIndex >= 0 && catchAllIndex < rules.Count - 1)
            result.Warnings.Add($"Rule '{rules[catchAllIndex].Name}' has no condition, so the rules after it can never match.");

        if (result.Errors.Count == 0 && catchAllIndex < 0) CheckCoverage(rules, fields, constants, result);

        if (rules.Any(r => r.Steps.Any(s => s.ApproverType is ApproverType.Manager or ApproverType.DepartmentHead)))
        {
            var noManager = await db.Users.Where(u => u.ManagerId == null).Select(u => u.Name).OrderBy(n => n).ToListAsync();
            if (noManager.Count > 0)
                result.Warnings.Add($"These people have no manager, so reports they submit to a Manager step will be refused: {string.Join(", ", noManager)}.");
        }
        return result;
    }

    /// <summary>
    /// Without a catch-all rule, proves every report matches some rule. Exact when rules only look at the total
    /// (the answer only changes at the amounts the rules mention); otherwise a catch-all is required.
    /// </summary>
    private static void CheckCoverage(List<WorkflowRule> rules, HashSet<string> fields, List<long> constants, ValidationResult result)
    {
        if (fields.Any(f => f != "totalCents"))
        {
            result.Errors.Add("These rules depend on more than the report total, so they can't be proven to cover every report. Add a final rule with no condition as a catch-all.");
            return;
        }

        var candidates = new SortedSet<long> { 1, 1_000_000_000_000 };
        foreach (var c in constants)
            foreach (var t in new[] { c - 1, c, c + 1 })
                if (t >= 1) candidates.Add(t);

        var uncovered = candidates
            .Where(t => !rules.Any(r => ConditionEvaluator.Evaluate(r.ConditionJson, new ConditionContext(t, 1, null, [], []))))
            .Take(3).ToList();
        foreach (var t in uncovered)
            result.Errors.Add($"No rule matches a report totalling {t / 100m:0.00}. Close the gap or add a catch-all rule.");
    }

    // ---- helpers ----

    private async Task EnsureNoDraftAsync(string name)
    {
        if (await db.WorkflowDefinitions.AnyAsync(d => d.Name == name && d.Status == WorkflowStatus.Draft))
            throw new EngineException(EngineError.Conflict, $"A draft of '{name}' already exists. Edit or delete it first.");
    }

    private static void RequireDraft(WorkflowDefinition d, string verb)
    {
        if (d.Status != WorkflowStatus.Draft)
            throw new EngineException(EngineError.Conflict, $"Only drafts can be {verb}. Create a new version to change a published workflow.");
    }

    private static void SetRules(WorkflowDefinition d, List<RuleRequest>? rules)
    {
        var priority = 1;
        foreach (var r in rules ?? [])
        {
            d.Rules.Add(new WorkflowRule
            {
                Priority = priority++,
                Name = r.Name.Trim(),
                ConditionJson = r.Condition is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } c ? c.GetRawText() : null,
                Steps = (r.Steps ?? []).Select((s, i) => new WorkflowStep
                {
                    Sequence = i + 1, ApproverType = s.ApproverType, ApproverParam = s.ApproverParam
                }).ToList()
            });
        }
    }
}
