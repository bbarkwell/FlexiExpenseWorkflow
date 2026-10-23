using ExpenseWorkflow.Api.Data;
using ExpenseWorkflow.Api.Dtos;
using ExpenseWorkflow.Api.Engine;
using ExpenseWorkflow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApprovalsController(AppDbContext db, CurrentUser currentUser, WorkflowEngine engine) : ControllerBase
{
    /// <summary>Active tasks I can decide: assigned to me (after delegation) or to a role I hold.</summary>
    [HttpGet("pending")]
    public async Task<ActionResult<List<PendingApprovalDto>>> Pending()
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        var roleIds = me.Roles.Select(r => r.Id).ToList();
        return await db.ApprovalTasks
            .Where(t => t.Status == Models.ApprovalTaskStatus.Active
                && t.Report.SubmitterId != me.Id
                && (t.EffectiveApproverId == me.Id
                    || (t.AssignedRoleId != null && roleIds.Contains(t.AssignedRoleId.Value))))
            .OrderBy(t => t.Report.SubmittedAt)
            .Select(t => new PendingApprovalDto(
                t.Id, t.ReportId, t.Report.Title, t.Report.Submitter.Name, t.Report.TotalCents,
                t.Report.SubmittedAt, t.Sequence, t.DelegatedFrom == null ? null : t.DelegatedFrom.Name))
            .ToListAsync();
    }

    [HttpPost("{taskId:int}/approve")]
    public Task<ActionResult<ReportDto>> Approve(int taskId, DecisionRequest? request) => Decide(taskId, true, request);

    [HttpPost("{taskId:int}/reject")]
    public Task<ActionResult<ReportDto>> Reject(int taskId, DecisionRequest? request) => Decide(taskId, false, request);

    private async Task<ActionResult<ReportDto>> Decide(int taskId, bool approve, DecisionRequest? request)
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        var report = await engine.DecideAsync(taskId, me, approve, request?.Comment);
        db.ChangeTracker.Clear();
        return ReportDto.From(await db.ExpenseReports.WithDetails().FirstAsync(r => r.Id == report.Id));
    }
}
