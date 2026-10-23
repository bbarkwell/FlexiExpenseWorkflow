using ExpenseWorkflow.Api.Engine;
using ExpenseWorkflow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Tests;

public class DelegationManagerTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task CreatesAndListsAWindow()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var (dana, mia) = (await h.UserAsync("Dana"), await h.UserAsync("Mia"));

        await h.Delegations.CreateAsync(dana, mia.Id, Today.AddDays(1), Today.AddDays(3));

        var list = await h.Delegations.ListAsync(dana.Id);
        Assert.Equal(mia.Id, Assert.Single(list).DelegateUserId);
    }

    [Fact]
    public async Task ListHidesWindowsThatAlreadyEnded()
    {
        using var h = await Harness.CreateAsync();
        var mark = await h.UserAsync("Mark");
        var d = await h.Db.Delegations.SingleAsync();
        d.StartsOn = Today.AddDays(-5);
        d.EndsOn = Today.AddDays(-1);
        await h.Db.SaveChangesAsync();

        Assert.Empty(await h.Delegations.ListAsync(mark.Id));
    }

    [Fact]
    public async Task RejectsSelfDelegation()
    {
        using var h = await Harness.CreateAsync();
        var dana = await h.UserAsync("Dana");
        var ex = await Assert.ThrowsAsync<EngineException>(() => h.Delegations.CreateAsync(dana, dana.Id, Today, Today));
        Assert.Equal(EngineError.Invalid, ex.Kind);
    }

    [Fact]
    public async Task RejectsInvertedDatesPastWindowsAndUnknownDelegates()
    {
        using var h = await Harness.CreateAsync();
        var dana = await h.UserAsync("Dana");
        var mia = await h.UserAsync("Mia");

        Assert.Equal(EngineError.Invalid, (await Assert.ThrowsAsync<EngineException>(
            () => h.Delegations.CreateAsync(dana, mia.Id, Today.AddDays(5), Today.AddDays(2)))).Kind);
        Assert.Equal(EngineError.Invalid, (await Assert.ThrowsAsync<EngineException>(
            () => h.Delegations.CreateAsync(dana, mia.Id, Today.AddDays(-5), Today.AddDays(-2)))).Kind);
        Assert.Equal(EngineError.Invalid, (await Assert.ThrowsAsync<EngineException>(
            () => h.Delegations.CreateAsync(dana, 9999, Today, Today))).Kind);
    }

    [Fact]
    public async Task RejectsOverlappingWindows()
    {
        using var h = await Harness.CreateAsync(); // Mark already has today..+14
        var (mark, frank) = (await h.UserAsync("Mark"), await h.UserAsync("Frank"));
        var ex = await Assert.ThrowsAsync<EngineException>(
            () => h.Delegations.CreateAsync(mark, frank.Id, Today.AddDays(10), Today.AddDays(20)));
        Assert.Equal(EngineError.Conflict, ex.Kind);

        // Touching-but-not-overlapping is fine.
        await h.Delegations.CreateAsync(mark, frank.Id, Today.AddDays(15), Today.AddDays(20));
    }

    [Fact]
    public async Task CannotRemoveSomeoneElsesWindow()
    {
        using var h = await Harness.CreateAsync();
        var id = (await h.Db.Delegations.SingleAsync()).Id;
        var dana = await h.UserAsync("Dana");
        var ex = await Assert.ThrowsAsync<EngineException>(() => h.Delegations.RemoveAsync(dana, id));
        Assert.Equal(EngineError.NotFound, ex.Kind);
    }

    [Fact]
    public async Task GoingOutOfOfficeMovesATaskThatIsAlreadyWaiting()
    {
        using var h = await Harness.CreateAsync();
        await h.ClearDelegationsAsync();
        var report = await h.SubmitAsync("Eve", 10_000);
        var (mark, mia) = (await h.UserAsync("Mark"), await h.UserAsync("Mia"));
        Assert.Equal(mark.Id, h.Active(report).EffectiveApproverId);

        await h.Delegations.CreateAsync(mark, mia.Id, Today, Today.AddDays(2));
        await h.Db.Entry(report).ReloadAsync();
        await h.Db.Entry(h.Active(report)).ReloadAsync();

        var task = h.Active(report);
        Assert.Equal(mia.Id, task.EffectiveApproverId);
        Assert.Equal(mark.Id, task.DelegatedFromId);
        Assert.Contains(await h.Db.AuditEvents.Where(a => a.ReportId == report.Id).Select(a => a.Type).ToListAsync(), t => t == "TaskReassigned");
    }

    [Fact]
    public async Task CancellingTheWindowReturnsTheTaskToTheOriginalApprover()
    {
        using var h = await Harness.CreateAsync(); // Mark -> Mia
        var report = await h.SubmitAsync("Eve", 10_000);
        var (mark, mia) = (await h.UserAsync("Mark"), await h.UserAsync("Mia"));
        Assert.Equal(mia.Id, h.Active(report).EffectiveApproverId);

        await h.Delegations.RemoveAsync(mark, (await h.Db.Delegations.AsNoTracking().SingleAsync()).Id);
        await h.Db.Entry(h.Active(report)).ReloadAsync();

        Assert.Equal(mark.Id, h.Active(report).EffectiveApproverId);
        Assert.Null(h.Active(report).DelegatedFromId);
    }
}
