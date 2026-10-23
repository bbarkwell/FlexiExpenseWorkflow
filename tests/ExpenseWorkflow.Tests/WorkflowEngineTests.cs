using ExpenseWorkflow.Api.Engine;
using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Tests;

public class WorkflowEngineTests
{
    // ---- Rule selection and the dollar bands ----

    [Theory]
    [InlineData(1L, "Under $500", 1)]
    [InlineData(49_999L, "Under $500", 1)]       // $499.99
    [InlineData(50_000L, "$500 to $5,000", 2)]   // $500.00 is in the middle band
    [InlineData(500_000L, "$500 to $5,000", 2)]  // $5,000.00 is still in the middle band
    [InlineData(500_001L, "Over $5,000", 3)]     // $5,000.01
    public async Task TotalPicksTheRightBand(long cents, string rule, int steps)
    {
        using var h = await Harness.CreateAsync();
        var report = await h.SubmitAsync("Eve", cents);

        Assert.Equal(ReportStatus.InReview, report.Status);
        Assert.Equal(rule, (await h.Db.WorkflowRules.FindAsync(report.MatchedRuleId))!.Name);
        Assert.Equal(steps, report.Tasks.Count);
    }

    [Fact]
    public async Task BandIsBasedOnTheTotalOfAllLines()
    {
        using var h = await Harness.CreateAsync();
        var report = await h.SubmitAsync("Eve", 30_000, 30_000); // two lines under $500, total $600
        Assert.Equal(2, report.Tasks.Count);
    }

    [Fact]
    public async Task ReportIsPinnedToTheWorkflowVersionInForce()
    {
        using var h = await Harness.CreateAsync();
        var report = await h.SubmitAsync("Eve", 10_000);
        var v1 = await h.Db.WorkflowDefinitions.SingleAsync();

        // Publishing a newer version later must not touch the in-flight report.
        h.Db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Name = v1.Name, Version = 2, Status = WorkflowStatus.Published, EffectiveFrom = DateTime.UtcNow,
            Rules = [new WorkflowRule { Priority = 1, Name = "Everything to Finance", Steps = [new WorkflowStep { Sequence = 1, ApproverType = ApproverType.Role, ApproverParam = "Finance" }] }]
        });
        await h.Db.SaveChangesAsync();

        Assert.Equal(v1.Id, report.WorkflowDefinitionId);
        var next = await h.SubmitAsync("Eve", 10_000);
        Assert.NotEqual(v1.Id, next.WorkflowDefinitionId);
    }

    [Fact]
    public async Task NoMatchingRuleIsRejectedAtSubmit()
    {
        using var h = await Harness.CreateAsync();
        h.Db.WorkflowRules.RemoveRange(h.Db.WorkflowRules.Where(r => r.Priority == 3));
        await h.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<EngineException>(() => h.SubmitAsync("Eve", 600_000));
        Assert.Equal(EngineError.Invalid, ex.Kind);
    }

    [Fact]
    public async Task EmptyReportCannotBeSubmitted()
    {
        using var h = await Harness.CreateAsync();
        var report = await h.DraftAsync("Eve");
        var ex = await Assert.ThrowsAsync<EngineException>(() => h.Engine.SubmitAsync(report));
        Assert.Equal(EngineError.Invalid, ex.Kind);
    }

    [Fact]
    public async Task OnlyDraftsCanBeSubmitted()
    {
        using var h = await Harness.CreateAsync();
        var report = await h.SubmitAsync("Eve", 10_000);
        var ex = await Assert.ThrowsAsync<EngineException>(() => h.Engine.SubmitAsync(report));
        Assert.Equal(EngineError.Conflict, ex.Kind);
    }

    // ---- Ordered approval flow ----

    [Fact]
    public async Task OverFiveThousandFlowsManagerThenHeadThenFinance()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var report = await h.SubmitAsync("Eve", 600_000);
        var (mark, dana, frank) = (await h.UserAsync("Mark"), await h.UserAsync("Dana"), await h.UserAsync("Frank"));

        Assert.Equal(mark.Id, h.Active(report).EffectiveApproverId);
        await h.Engine.DecideAsync(h.Active(report).Id, mark, true, null);

        Assert.Equal(dana.Id, h.Active(report).EffectiveApproverId);
        await h.Engine.DecideAsync(h.Active(report).Id, dana, true, null);

        Assert.Equal(h.Db.Roles.Single(r => r.Name == "Finance").Id, h.Active(report).AssignedRoleId);
        Assert.Equal(ReportStatus.InReview, report.Status);
        await h.Engine.DecideAsync(h.Active(report).Id, frank, true, "ok");

        Assert.Equal(ReportStatus.Approved, report.Status);
        Assert.All(report.Tasks, t => Assert.Equal(ApprovalTaskStatus.Approved, t.Status));
        Assert.Equal(frank.Id, report.Tasks.Single(t => t.AssignedRoleId != null).DecidedById);
    }

    [Fact]
    public async Task LaterStepsCannotBeDecidedOutOfOrder()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var report = await h.SubmitAsync("Eve", 100_000);
        var dana = await h.UserAsync("Dana");
        var danaTask = report.Tasks.Single(t => t.Sequence == 2);

        var ex = await Assert.ThrowsAsync<EngineException>(() => h.Engine.DecideAsync(danaTask.Id, dana, true, null));
        Assert.Equal(EngineError.Conflict, ex.Kind);
    }

    [Fact]
    public async Task OnlyTheAssignedApproverCanDecide()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var report = await h.SubmitAsync("Eve", 10_000);
        var dana = await h.UserAsync("Dana");
        var ex = await Assert.ThrowsAsync<EngineException>(
            () => h.Engine.DecideAsync(h.Active(report).Id, dana, true, null));
        Assert.Equal(EngineError.Forbidden, ex.Kind);
    }

    [Fact]
    public async Task RoleStepRejectsNonHolders()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var report = await h.SubmitAsync("Eve", 600_000);
        await h.Engine.DecideAsync(h.Active(report).Id, await h.UserAsync("Mark"), true, null);
        await h.Engine.DecideAsync(h.Active(report).Id, await h.UserAsync("Dana"), true, null);

        var mia = await h.UserAsync("Mia");
        var ex = await Assert.ThrowsAsync<EngineException>(
            () => h.Engine.DecideAsync(h.Active(report).Id, mia, true, null));
        Assert.Equal(EngineError.Forbidden, ex.Kind);
    }

    [Fact]
    public async Task SubmitterCanNeverDecideOwnReport()
    {
        using var h = await Harness.CreateAsync();
        // Frank holds Finance but has no manager; give him one so his own report can route.
        var frank = await h.UserAsync("Frank");
        frank.ManagerId = (await h.UserAsync("Dana")).Id;
        frank.DepartmentId = (await h.UserAsync("Dana")).DepartmentId;
        // A second Finance holder must exist, otherwise the engine refuses to route Frank's report at all.
        (await h.UserAsync("Alice")).Roles.Add(h.Db.Roles.Single(r => r.Name == "Finance"));
        await h.Db.SaveChangesAsync();
        await h.ClearDelegationsAsync();

        var report = await h.SubmitAsync("Frank", 600_000);
        await h.Engine.DecideAsync(h.Active(report).Id, await h.UserAsync("Dana"), true, null); // manager; head is the same person
        var ex = await Assert.ThrowsAsync<EngineException>(
            () => h.Engine.DecideAsync(h.Active(report).Id, frank, true, null));
        Assert.Equal(EngineError.Forbidden, ex.Kind);
    }

    // ---- Rejection and withdrawal ----

    [Fact]
    public async Task RejectionNeedsACommentAndEndsTheChain()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var report = await h.SubmitAsync("Eve", 100_000);
        var mark = await h.UserAsync("Mark");

        var ex = await Assert.ThrowsAsync<EngineException>(() => h.Engine.DecideAsync(h.Active(report).Id, mark, false, " "));
        Assert.Equal(EngineError.Invalid, ex.Kind);

        await h.Engine.DecideAsync(h.Active(report).Id, mark, false, "Not allowed");
        Assert.Equal(ReportStatus.Rejected, report.Status);
        Assert.Equal(ApprovalTaskStatus.Rejected, report.Tasks.Single(t => t.Sequence == 1).Status);
        Assert.Equal(ApprovalTaskStatus.Skipped, report.Tasks.Single(t => t.Sequence == 2).Status);
    }

    [Fact]
    public async Task WithdrawClosesOpenTasks()
    {
        using var h = await Harness.CreateAsync();
        var report = await h.SubmitAsync("Eve", 100_000);
        await h.Engine.WithdrawAsync(report, await h.UserAsync("Eve"));

        Assert.Equal(ReportStatus.Withdrawn, report.Status);
        Assert.All(report.Tasks, t => Assert.Equal(ApprovalTaskStatus.Skipped, t.Status));
    }

    [Fact]
    public async Task OnlySubmitterCanWithdraw()
    {
        using var h = await Harness.CreateAsync();
        var report = await h.SubmitAsync("Eve", 100_000);
        var ex = await Assert.ThrowsAsync<EngineException>(() => h.Engine.WithdrawAsync(report, h.Db.Users.Include(u => u.Roles).First(u => u.Email == "mark@acme.test")));
        Assert.Equal(EngineError.Forbidden, ex.Kind);
    }

    // ---- Delegation ----

    [Fact]
    public async Task OutOfOfficeApproverIsReplacedByDelegate()
    {
        using var h = await Harness.CreateAsync(); // seed: Mark is out, Mia covers
        var report = await h.SubmitAsync("Eve", 10_000);
        var (mark, mia) = (await h.UserAsync("Mark"), await h.UserAsync("Mia"));

        var task = h.Active(report);
        Assert.Equal(mark.Id, task.ResolvedApproverId);
        Assert.Equal(mia.Id, task.EffectiveApproverId);
        Assert.Equal(mark.Id, task.DelegatedFromId);

        // Mark himself is no longer the one who decides.
        var ex = await Assert.ThrowsAsync<EngineException>(() => h.Engine.DecideAsync(task.Id, mark, true, null));
        Assert.Equal(EngineError.Forbidden, ex.Kind);

        await h.Engine.DecideAsync(task.Id, mia, true, null);
        Assert.Equal(ReportStatus.Approved, report.Status);
        Assert.Equal(mia.Id, task.DecidedById);
    }

    [Fact]
    public async Task ExpiredOrFutureDelegationIsIgnored()
    {
        using var h = await Harness.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var d = await h.Db.Delegations.SingleAsync();
        d.StartsOn = today.AddDays(-10);
        d.EndsOn = today.AddDays(-1);
        await h.Db.SaveChangesAsync();

        var report = await h.SubmitAsync("Eve", 10_000);
        Assert.Null(h.Active(report).DelegatedFromId);
        Assert.Equal((await h.UserAsync("Mark")).Id, h.Active(report).EffectiveApproverId);
    }

    [Fact]
    public async Task DelegationIsResolvedWhenTheStepActivatesNotAtSubmit()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var report = await h.SubmitAsync("Eve", 100_000); // Mark, then Dana
        var (mark, dana, mia) = (await h.UserAsync("Mark"), await h.UserAsync("Dana"), await h.UserAsync("Mia"));

        // Dana goes out of office after the report was submitted.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        h.Db.Delegations.Add(new Delegation { UserId = dana.Id, DelegateUserId = mia.Id, StartsOn = today, EndsOn = today });
        await h.Db.SaveChangesAsync();

        await h.Engine.DecideAsync(h.Active(report).Id, mark, true, null);
        Assert.Equal(mia.Id, h.Active(report).EffectiveApproverId);
        Assert.Equal(dana.Id, h.Active(report).DelegatedFromId);
    }

    [Fact]
    public async Task DelegateChainsAreFollowed()
    {
        using var h = await Harness.CreateAsync(); // Mark -> Mia
        var (mia, frank) = (await h.UserAsync("Mia"), await h.UserAsync("Frank"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        h.Db.Delegations.Add(new Delegation { UserId = mia.Id, DelegateUserId = frank.Id, StartsOn = today, EndsOn = today });
        await h.Db.SaveChangesAsync();

        var report = await h.SubmitAsync("Eve", 10_000);
        Assert.Equal(frank.Id, h.Active(report).EffectiveApproverId);
    }

    [Fact]
    public async Task DelegationLoopFallsBackToTheApproversManager()
    {
        using var h = await Harness.CreateAsync(); // Mark -> Mia
        var (mark, mia, dana) = (await h.UserAsync("Mark"), await h.UserAsync("Mia"), await h.UserAsync("Dana"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        h.Db.Delegations.Add(new Delegation { UserId = mia.Id, DelegateUserId = mark.Id, StartsOn = today, EndsOn = today });
        await h.Db.SaveChangesAsync();

        var report = await h.SubmitAsync("Eve", 10_000);
        Assert.Equal(dana.Id, h.Active(report).EffectiveApproverId);
    }

    [Fact]
    public async Task DelegateWhoIsTheSubmitterIsNotUsed()
    {
        using var h = await Harness.CreateAsync();
        var (mark, mia) = (await h.UserAsync("Mark"), await h.UserAsync("Mia"));
        // Mia submits; her manager is Dana. Make Dana's delegate Mia.
        await h.ClearDelegationsAsync();
        var dana = await h.UserAsync("Dana");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        h.Db.Delegations.Add(new Delegation { UserId = dana.Id, DelegateUserId = mia.Id, StartsOn = today, EndsOn = today });
        await h.Db.SaveChangesAsync();

        var report = await h.SubmitAsync("Mia", 10_000);
        Assert.Equal(dana.Id, h.Active(report).EffectiveApproverId); // no manager above Dana, so Dana stays
        Assert.NotEqual(mia.Id, h.Active(report).EffectiveApproverId);
    }

    // ---- Skipping and unresolvable chains ----

    [Fact]
    public async Task SamePersonInConsecutiveStepsApprovesOnce()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var report = await h.SubmitAsync("Mark", 100_000); // manager is Dana, and so is the department head
        var dana = await h.UserAsync("Dana");

        Assert.Equal(ApprovalTaskStatus.Skipped, report.Tasks.Single(t => t.Sequence == 2).Status);
        await h.Engine.DecideAsync(h.Active(report).Id, dana, true, null);
        Assert.Equal(ReportStatus.Approved, report.Status);
    }

    [Fact]
    public async Task MissingManagerFailsAtSubmit()
    {
        using var h = await Harness.CreateAsync();
        var ex = await Assert.ThrowsAsync<EngineException>(() => h.SubmitAsync("Dana", 10_000));
        Assert.Equal(EngineError.Invalid, ex.Kind);
        Assert.Contains("no manager", ex.Message);
    }

    [Fact]
    public async Task AuditTrailRecordsTheFlow()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var report = await h.SubmitAsync("Eve", 10_000);
        await h.Engine.DecideAsync(h.Active(report).Id, await h.UserAsync("Mark"), true, null);

        var types = await h.Db.AuditEvents.Where(a => a.ReportId == report.Id).OrderBy(a => a.Id).Select(a => a.Type).ToListAsync();
        Assert.Equal(["Submitted", "StepActivated", "Approved", "ReportApproved"], types);
    }
}

public class RoleRoutingTests
{
    [Fact]
    public async Task RoleStepIsRefusedWhenOnlyTheSubmitterHoldsTheRole()
    {
        using var h = await Harness.CreateAsync();
        var frank = await h.UserAsync("Frank");
        frank.ManagerId = (await h.UserAsync("Dana")).Id;
        frank.DepartmentId = (await h.UserAsync("Dana")).DepartmentId;
        await h.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<EngineException>(() => h.SubmitAsync("Frank", 600_000));
        Assert.Equal(EngineError.Invalid, ex.Kind);
    }
}
