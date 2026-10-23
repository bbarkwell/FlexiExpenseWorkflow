using ExpenseWorkflow.Api.Data;
using ExpenseWorkflow.Api.Dtos;
using ExpenseWorkflow.Api.Engine;
using ExpenseWorkflow.Api.Models;
using ExpenseWorkflow.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseWorkflow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController(AppDbContext db, CurrentUser currentUser, WorkflowEngine engine) : ControllerBase
{
    /// <summary>My reports, newest first. Optional ?status=Draft filter.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ReportDto>>> Mine([FromQuery] ReportStatus? status)
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        var q = db.ExpenseReports.WithDetails().Where(r => r.SubmitterId == me.Id);
        if (status is { } s) q = q.Where(r => r.Status == s);
        var reports = await q.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return reports.Select(ReportDto.From).ToList();
    }

    /// <summary>Visible to the submitter and to anyone who is, was, or may be asked to approve it.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ReportDto>> Get(int id)
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        var roleIds = me.Roles.Select(r => r.Id).ToList();
        var report = await db.ExpenseReports.WithDetails().FirstOrDefaultAsync(r => r.Id == id
            && (r.SubmitterId == me.Id || r.Tasks.Any(t =>
                t.ResolvedApproverId == me.Id || t.EffectiveApproverId == me.Id || t.DecidedById == me.Id
                || (t.AssignedRoleId != null && roleIds.Contains(t.AssignedRoleId.Value)))));
        return report is null ? NotFound() : ReportDto.From(report);
    }

    [HttpPost]
    public async Task<ActionResult<ReportDto>> Create(SaveReportRequest request)
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        var report = new ExpenseReport { SubmitterId = me.Id, Submitter = me, Title = request.Title };
        ApplyLines(report, request.Lines);
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = report.Id }, ReportDto.From(report));
    }

    /// <summary>Replaces the title and the full set of lines. Drafts only.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ReportDto>> Update(int id, SaveReportRequest request)
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        var report = await db.ExpenseReports.WithDetails().FirstOrDefaultAsync(r => r.Id == id && r.SubmitterId == me.Id);
        if (report is null) return NotFound();
        if (report.Status != ReportStatus.Draft) return Conflict("Only draft reports can be edited.");

        report.Title = request.Title;
        db.ExpenseLines.RemoveRange(report.Lines);
        report.Lines.Clear();
        ApplyLines(report, request.Lines);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict("The report was changed by someone else. Reload and retry."); }
        return ReportDto.From(report);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        var report = await db.ExpenseReports.FirstOrDefaultAsync(r => r.Id == id && r.SubmitterId == me.Id);
        if (report is null) return NotFound();
        if (report.Status != ReportStatus.Draft) return Conflict("Only draft reports can be deleted.");
        db.ExpenseReports.Remove(report);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Runs the workflow engine: picks the rule, plans the chain, activates the first step.</summary>
    [HttpPost("{id:int}/submit")]
    public async Task<ActionResult<ReportDto>> Submit(int id)
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        var report = await db.ExpenseReports.WithDetails().FirstOrDefaultAsync(r => r.Id == id && r.SubmitterId == me.Id);
        if (report is null) return NotFound();
        await engine.SubmitAsync(report);
        return await Reload(id);
    }

    [HttpPost("{id:int}/withdraw")]
    public async Task<ActionResult<ReportDto>> Withdraw(int id)
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        var report = await db.ExpenseReports.WithDetails().FirstOrDefaultAsync(r => r.Id == id && r.SubmitterId == me.Id);
        if (report is null) return NotFound();
        await engine.WithdrawAsync(report, me);
        return await Reload(id);
    }

    private async Task<ReportDto> Reload(int id)
    {
        db.ChangeTracker.Clear();
        return ReportDto.From(await db.ExpenseReports.WithDetails().FirstAsync(r => r.Id == id));
    }

    private static void ApplyLines(ExpenseReport report, List<LineRequest>? lines)
    {
        foreach (var l in lines ?? [])
            report.Lines.Add(new ExpenseLine { Date = l.Date, Category = l.Category, AmountCents = l.AmountCents, Description = l.Description });
        report.TotalCents = report.Lines.Sum(l => l.AmountCents);
    }
}
