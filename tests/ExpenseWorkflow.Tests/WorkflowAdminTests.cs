using System.Text.Json;
using ExpenseWorkflow.Api.Dtos;
using ExpenseWorkflow.Api.Engine;
using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Tests;

public class WorkflowAdminTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static RuleRequest Rule(string name, string? condition, params StepRequest[] steps) =>
        new(name, condition is null ? null : Json(condition), steps.ToList());

    private static StepRequest Manager => new(ApproverType.Manager, null);
    private static StepRequest Head => new(ApproverType.DepartmentHead, null);
    private static StepRequest Role(string name) => new(ApproverType.Role, name);

    private static string Total(string op, long cents) => $$"""{ "field": "totalCents", "op": "{{op}}", "value": {{cents}} }""";

    // ---- validation ----

    [Fact]
    public async Task TheSeededWorkflowIsValidAndProvablyCoversEveryTotal()
    {
        using var h = await Harness.CreateAsync();
        var result = await h.Workflows.ValidateAsync((await h.Db.WorkflowDefinitions.SingleAsync()).Id);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task GapBetweenBandsIsReported()
    {
        using var h = await Harness.CreateAsync();
        var draft = await h.Workflows.CreateDraftAsync("Gappy",
        [
            Rule("Small", Total("lt", 50_000), Manager),
            Rule("Large", Total("gt", 60_000), Manager, Head)   // $500.00 to $600.00 is uncovered
        ]);
        var result = await h.Workflows.ValidateAsync(draft.Id);
        Assert.Contains(result.Errors, e => e.Contains("500.00"));
    }

    [Fact]
    public async Task OtherFieldsNeedACatchAll()
    {
        using var h = await Harness.CreateAsync();
        var onlyDept = await h.Workflows.CreateDraftAsync("ByDept",
            [Rule("Eng", """{ "field": "department", "op": "eq", "value": "Engineering" }""", Manager)]);
        Assert.Contains((await h.Workflows.ValidateAsync(onlyDept.Id)).Errors, e => e.Contains("catch-all"));

        await h.Workflows.SaveRulesAsync(onlyDept.Id,
        [
            Rule("Eng", """{ "field": "department", "op": "eq", "value": "Engineering" }""", Manager),
            Rule("Everyone else", null, Manager, Head)
        ]);
        Assert.Empty((await h.Workflows.ValidateAsync(onlyDept.Id)).Errors);
    }

    [Fact]
    public async Task ReportsEveryProblemNotJustTheFirst()
    {
        using var h = await Harness.CreateAsync();
        var draft = await h.Workflows.CreateDraftAsync("Broken",
        [
            Rule("Bad op", """{ "field": "totalCents", "op": "contains", "value": 1 }""", Manager),
            Rule("No steps", null),
            Rule("Bad role", null, Role("Nobody")),
            Rule("Bad user", null, new StepRequest(ApproverType.SpecificUser, "9999"))
        ]);
        var errors = (await h.Workflows.ValidateAsync(draft.Id)).Errors;

        Assert.Contains(errors, e => e.Contains("Bad op") && e.Contains("contains"));
        Assert.Contains(errors, e => e.Contains("No steps") && e.Contains("no approval steps"));
        Assert.Contains(errors, e => e.Contains("Nobody"));
        Assert.Contains(errors, e => e.Contains("named user"));
    }

    [Fact]
    public async Task EmptyDraftCannotBePublished()
    {
        using var h = await Harness.CreateAsync();
        var draft = await h.Workflows.CreateDraftAsync("Empty", null);
        var ex = await Assert.ThrowsAsync<WorkflowValidationException>(() => h.Workflows.PublishAsync(draft.Id, null));
        Assert.Contains(ex.Result.Errors, e => e.Contains("at least one rule"));
    }

    [Fact]
    public async Task CatchAllBeforeOtherRulesWarns()
    {
        using var h = await Harness.CreateAsync();
        var draft = await h.Workflows.CreateDraftAsync("Shadowed",
            [Rule("Everything", null, Manager), Rule("Never used", Total("gt", 1), Manager, Head)]);
        var result = await h.Workflows.ValidateAsync(draft.Id);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Contains("can never match"));
    }

    // ---- publishing and versioning ----

    [Fact]
    public async Task PublishingMakesTheNewVersionCurrentAndLeavesInFlightReportsAlone()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var inFlight = await h.SubmitAsync("Eve", 10_000); // v1: Manager only
        var v1 = await h.Db.WorkflowDefinitions.SingleAsync();

        var draft = await h.Workflows.NewVersionAsync(v1.Id);
        Assert.Equal(2, draft.Version);
        await h.Workflows.SaveRulesAsync(draft.Id, [Rule("All to Finance", null, Role("Finance"))]);
        await h.Workflows.PublishAsync(draft.Id, null);

        Assert.Equal(draft.Id, await h.Workflows.CurrentIdAsync());
        Assert.Equal(v1.Id, inFlight.WorkflowDefinitionId);
        Assert.Equal((await h.UserAsync("Mark")).Id, h.Active(inFlight).EffectiveApproverId); // unchanged

        var fresh = await h.SubmitAsync("Eve", 10_000);
        Assert.Equal(draft.Id, fresh.WorkflowDefinitionId);
        Assert.NotNull(h.Active(fresh).AssignedRoleId);
    }

    [Fact]
    public async Task FutureEffectiveDateSchedulesTheCutover()
    {
        using var h = await Harness.CreateAsync();
        var v1 = await h.Db.WorkflowDefinitions.SingleAsync();
        var draft = await h.Workflows.NewVersionAsync(v1.Id);

        await h.Workflows.PublishAsync(draft.Id, DateTime.UtcNow.AddDays(7));

        Assert.Equal(v1.Id, await h.Workflows.CurrentIdAsync()); // still v1 until the date arrives
    }

    [Fact]
    public async Task CannotPublishWithAPastEffectiveDateOrBehindALaterScheduledVersion()
    {
        using var h = await Harness.CreateAsync();
        var v1 = await h.Db.WorkflowDefinitions.SingleAsync();

        var past = await h.Workflows.NewVersionAsync(v1.Id);
        var ex = await Assert.ThrowsAsync<WorkflowValidationException>(() => h.Workflows.PublishAsync(past.Id, DateTime.UtcNow.AddDays(-2)));
        Assert.Contains(ex.Result.Errors, e => e.Contains("past"));
        await h.Workflows.DeleteDraftAsync(past.Id);

        var later = await h.Workflows.NewVersionAsync(v1.Id);
        await h.Workflows.PublishAsync(later.Id, DateTime.UtcNow.AddDays(30));
        var sooner = await h.Workflows.NewVersionAsync(later.Id);
        var ex2 = await Assert.ThrowsAsync<WorkflowValidationException>(() => h.Workflows.PublishAsync(sooner.Id, DateTime.UtcNow.AddDays(10)));
        Assert.Contains(ex2.Result.Errors, e => e.Contains("later version"));
    }

    [Fact]
    public async Task PublishedVersionsAreImmutable()
    {
        using var h = await Harness.CreateAsync();
        var v1 = await h.Db.WorkflowDefinitions.SingleAsync();

        Assert.Equal(EngineError.Conflict, (await Assert.ThrowsAsync<EngineException>(() => h.Workflows.SaveRulesAsync(v1.Id, []))).Kind);
        Assert.Equal(EngineError.Conflict, (await Assert.ThrowsAsync<EngineException>(() => h.Workflows.DeleteDraftAsync(v1.Id))).Kind);
        Assert.Equal(EngineError.Conflict, (await Assert.ThrowsAsync<EngineException>(() => h.Workflows.PublishAsync(v1.Id, null))).Kind);
    }

    [Fact]
    public async Task OnlyOneDraftPerWorkflowAtATime()
    {
        using var h = await Harness.CreateAsync();
        var v1 = await h.Db.WorkflowDefinitions.SingleAsync();
        await h.Workflows.NewVersionAsync(v1.Id);
        var ex = await Assert.ThrowsAsync<EngineException>(() => h.Workflows.NewVersionAsync(v1.Id));
        Assert.Equal(EngineError.Conflict, ex.Kind);
    }

    [Fact]
    public async Task NewVersionCopiesRulesAndStepsInOrder()
    {
        using var h = await Harness.CreateAsync();
        var v1 = await h.Workflows.GetAsync((await h.Db.WorkflowDefinitions.SingleAsync()).Id);
        var copy = await h.Workflows.NewVersionAsync(v1.Id);

        Assert.Equal(v1.Rules.OrderBy(r => r.Priority).Select(r => r.Name), copy.Rules.OrderBy(r => r.Priority).Select(r => r.Name));
        Assert.Equal(6, copy.Rules.Sum(r => r.Steps.Count));
        Assert.Equal(WorkflowStatus.Draft, copy.Status);
    }

    [Fact]
    public async Task SavingRulesReplacesThemAndOrderBecomesPriority()
    {
        using var h = await Harness.CreateAsync();
        var draft = await h.Workflows.CreateDraftAsync("Order", [Rule("A", Total("lt", 10), Manager), Rule("B", null, Manager, Head)]);
        await h.Workflows.SaveRulesAsync(draft.Id, [Rule("Z", null, Head), Rule("Y", Total("lt", 10), Manager)]);

        var rules = (await h.Workflows.GetAsync(draft.Id)).Rules.OrderBy(r => r.Priority).ToList();
        Assert.Equal(["Z", "Y"], rules.Select(r => r.Name));
        Assert.Equal([1, 2], rules.Select(r => r.Priority));
    }

    // ---- simulation ----

    [Fact]
    public async Task SimulateShowsTheChainWithoutCreatingAnything()
    {
        using var h = await Harness.CreateAsync(); // Mark is out; Mia covers
        var def = await h.Db.WorkflowDefinitions.SingleAsync();
        var eve = await h.UserAsync("Eve");
        var reportsBefore = await h.Db.ExpenseReports.CountAsync();
        var auditBefore = await h.Db.AuditEvents.CountAsync();

        var sim = await h.Workflows.SimulateAsync(def.Id, new SimulateRequest(eve.Id, 600_000, null, null));

        Assert.Equal("Over $5,000", sim.RuleName);
        Assert.Equal(3, sim.Steps.Count);
        Assert.Equal(["Mark Manager"], sim.Steps[0].Approvers);
        Assert.Equal("Mia Manager", sim.Steps[0].ActingToday);
        Assert.Equal(["Dana Head"], sim.Steps[1].Approvers);
        Assert.Equal(ApproverType.Role, sim.Steps[2].ApproverType);
        Assert.Equal(["Frank Finance"], sim.Steps[2].Approvers);
        Assert.Equal(reportsBefore, await h.Db.ExpenseReports.CountAsync());
        Assert.Equal(auditBefore, await h.Db.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task SimulateWorksOnADraftAndShowsSkippedSteps()
    {
        using var h = await Harness.CreateAsync();
        var draft = await h.Workflows.CreateDraftAsync("Trial", [Rule("All", null, Manager, Head)]);
        var mark = await h.UserAsync("Mark"); // manager Dana == department head Dana

        var sim = await h.Workflows.SimulateAsync(draft.Id, new SimulateRequest(mark.Id, 1_000, null, null));

        Assert.False(sim.Steps[0].Skipped);
        Assert.True(sim.Steps[1].Skipped);
        Assert.Contains("same approver", sim.Steps[1].SkipReason);
    }

    [Fact]
    public async Task SimulateExplainsUnresolvableChains()
    {
        using var h = await Harness.CreateAsync();
        var def = await h.Db.WorkflowDefinitions.SingleAsync();
        var dana = await h.UserAsync("Dana"); // no manager
        var ex = await Assert.ThrowsAsync<EngineException>(() => h.Workflows.SimulateAsync(def.Id, new SimulateRequest(dana.Id, 1_000, null, null)));
        Assert.Contains("no manager", ex.Message);
    }

    [Fact]
    public async Task SimulatedRuleMatchesWhatSubmitWouldPick()
    {
        using var h = await Harness.CreateAsync();
        var def = await h.Db.WorkflowDefinitions.SingleAsync();
        var eve = await h.UserAsync("Eve");
        foreach (var cents in new long[] { 49_999, 50_000, 500_000, 500_001 })
        {
            var sim = await h.Workflows.SimulateAsync(def.Id, new SimulateRequest(eve.Id, cents, null, null));
            var report = await h.SubmitAsync("Eve", cents);
            Assert.Equal((await h.Db.WorkflowRules.FindAsync(report.MatchedRuleId))!.Name, sim.RuleName);
        }
    }
}

public class OrgAdminTests
{
    [Fact]
    public async Task CreatesAUserWithRolesAndReportingLine()
    {
        using var h = await Harness.CreateAsync();
        var dana = await h.UserAsync("Dana");
        var user = await h.Org.CreateUserAsync(new UserRequest("New Hire", "new@acme.test", dana.DepartmentId, dana.Id, ["Employee", "Finance"]));

        Assert.Equal("Dana Head", user.Manager!.Name);
        Assert.Equal(["Employee", "Finance"], user.Roles.Select(r => r.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task RejectsDuplicateEmailUnknownRolesAndMissingReferences()
    {
        using var h = await Harness.CreateAsync();
        Assert.Equal(EngineError.Conflict, (await Assert.ThrowsAsync<EngineException>(
            () => h.Org.CreateUserAsync(new UserRequest("X", "dana@acme.test", null, null, [])))).Kind);
        Assert.Equal(EngineError.Invalid, (await Assert.ThrowsAsync<EngineException>(
            () => h.Org.CreateUserAsync(new UserRequest("X", "x@acme.test", null, null, ["Wizard"])))).Kind);
        Assert.Equal(EngineError.Invalid, (await Assert.ThrowsAsync<EngineException>(
            () => h.Org.CreateUserAsync(new UserRequest("X", "x@acme.test", null, 9999, [])))).Kind);
        Assert.Equal(EngineError.Invalid, (await Assert.ThrowsAsync<EngineException>(
            () => h.Org.CreateUserAsync(new UserRequest("X", "x@acme.test", 9999, null, [])))).Kind);
    }

    [Fact]
    public async Task RejectsReportingLoops()
    {
        using var h = await Harness.CreateAsync(); // Eve -> Mark -> Dana
        var (eve, dana) = (await h.UserAsync("Eve"), await h.UserAsync("Dana"));

        var ex = await Assert.ThrowsAsync<EngineException>(
            () => h.Org.UpdateUserAsync(dana.Id, new UserRequest(dana.Name, dana.Email, dana.DepartmentId, eve.Id, ["Employee"])));
        Assert.Contains("loop", ex.Message);

        var self = await Assert.ThrowsAsync<EngineException>(
            () => h.Org.UpdateUserAsync(dana.Id, new UserRequest(dana.Name, dana.Email, dana.DepartmentId, dana.Id, ["Employee"])));
        Assert.Contains("loop", self.Message);
    }

    [Fact]
    public async Task ChangingAManagerChangesWhoNewReportsRouteTo()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var (eve, mia) = (await h.UserAsync("Eve"), await h.UserAsync("Mia"));
        await h.Org.UpdateUserAsync(eve.Id, new UserRequest(eve.Name, eve.Email, eve.DepartmentId, mia.Id, ["Employee"]));

        var report = await h.SubmitAsync("Eve", 10_000);
        Assert.Equal(mia.Id, h.Active(report).EffectiveApproverId);
    }

    [Fact]
    public async Task CreatesRolesOnceAndDepartmentsWithHeads()
    {
        using var h = await Harness.CreateAsync();
        await h.Org.CreateRoleAsync("Controller");
        var dup = await Assert.ThrowsAsync<EngineException>(() => h.Org.CreateRoleAsync("Controller "));
        Assert.Equal(EngineError.Conflict, dup.Kind);

        var frank = await h.UserAsync("Frank");
        var dept = await h.Org.CreateDepartmentAsync(new DepartmentRequest("Finance", frank.Id));
        Assert.Equal("Frank Finance", dept.HeadUser!.Name);
    }
}
