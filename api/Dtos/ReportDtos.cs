using System.ComponentModel.DataAnnotations;
using ExpenseWorkflow.Api.Models;

namespace ExpenseWorkflow.Api.Dtos;

public record LineRequest(
    DateOnly Date,
    [Required, StringLength(100)] string Category,
    [Range(1, long.MaxValue)] long AmountCents,
    [StringLength(500)] string Description = "");

public record SaveReportRequest(
    [Required, StringLength(200)] string Title,
    List<LineRequest>? Lines);

public record DecisionRequest([StringLength(1000)] string? Comment);

public record LineDto(int Id, DateOnly Date, string Category, long AmountCents, string Description);

public record TaskDto(
    int Id,
    int Sequence,
    string Status,
    string? AssignedRole,
    string? ResolvedApprover,
    string? EffectiveApprover,
    string? DelegatedFrom,
    string? DecidedBy,
    DateTime? DecidedAt,
    string? Comment);

public record ReportDto(
    int Id,
    int SubmitterId,
    string SubmitterName,
    string Title,
    long TotalCents,
    string Status,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    List<LineDto> Lines,
    List<TaskDto> Tasks)
{
    public static ReportDto From(ExpenseReport r) => new(
        r.Id, r.SubmitterId, r.Submitter.Name, r.Title, r.TotalCents, r.Status.ToString(),
        r.CreatedAt, r.SubmittedAt,
        r.Lines.OrderBy(l => l.Date).ThenBy(l => l.Id)
            .Select(l => new LineDto(l.Id, l.Date, l.Category, l.AmountCents, l.Description)).ToList(),
        r.Tasks.OrderBy(t => t.Sequence)
            .Select(t => new TaskDto(
                t.Id, t.Sequence, t.Status.ToString(), t.AssignedRole?.Name, t.ResolvedApprover?.Name,
                t.EffectiveApprover?.Name, t.DelegatedFrom?.Name, t.DecidedBy?.Name, t.DecidedAt, t.Comment)).ToList());
}

public record PendingApprovalDto(
    int TaskId,
    int ReportId,
    string Title,
    string SubmitterName,
    long TotalCents,
    DateTime? SubmittedAt,
    int Sequence,
    string? DelegatedFrom);
