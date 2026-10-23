using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ExpenseWorkflow.Api.Models;

namespace ExpenseWorkflow.Api.Dtos;

// ---- Delegations ----

public record CreateDelegationRequest(int DelegateUserId, DateOnly StartsOn, DateOnly EndsOn);

public record DelegationDto(int Id, int DelegateUserId, string DelegateName, DateOnly StartsOn, DateOnly EndsOn)
{
    public static DelegationDto From(Delegation d) => new(d.Id, d.DelegateUserId, d.DelegateUser.Name, d.StartsOn, d.EndsOn);
}

// ---- Workflows ----

public record StepRequest(ApproverType ApproverType, string? ApproverParam);

/// <summary>Rules are evaluated in the order given; a null Condition matches everything.</summary>
public record RuleRequest([Required, StringLength(100)] string Name, JsonElement? Condition, List<StepRequest>? Steps);

public record CreateWorkflowRequest([Required, StringLength(100)] string Name, List<RuleRequest>? Rules);

public record SaveRulesRequest(List<RuleRequest>? Rules);

public record PublishRequest(DateTime? EffectiveFrom);

public record SimulateRequest(
    int SubmitterId,
    [Range(1, long.MaxValue)] long TotalCents,
    List<string>? Categories,
    [Range(1, int.MaxValue)] int? LineCount);

public record StepDto(int Sequence, ApproverType ApproverType, string? ApproverParam);

public record RuleDto(int Id, int Priority, string Name, JsonElement? Condition, List<StepDto> Steps);

public record WorkflowDto(
    int Id, string Name, int Version, string Status, DateTime? EffectiveFrom, bool IsCurrent, List<RuleDto> Rules)
{
    public static WorkflowDto From(WorkflowDefinition d, bool isCurrent) => new(
        d.Id, d.Name, d.Version, d.Status.ToString(), d.EffectiveFrom, isCurrent,
        d.Rules.OrderBy(r => r.Priority).Select(r => new RuleDto(
            r.Id, r.Priority, r.Name,
            string.IsNullOrWhiteSpace(r.ConditionJson) ? null : JsonDocument.Parse(r.ConditionJson).RootElement.Clone(),
            r.Steps.OrderBy(s => s.Sequence).Select(s => new StepDto(s.Sequence, s.ApproverType, s.ApproverParam)).ToList())).ToList());
}

public record ValidationDto(List<string> Errors, List<string> Warnings);

public record SimulatedStepDto(
    int Sequence, ApproverType ApproverType, List<string> Approvers, bool Skipped, string? SkipReason, string? ActingToday);

public record SimulationDto(string RuleName, int RulePriority, List<SimulatedStepDto> Steps);

// ---- Org chart ----

public record UserRequest(
    [Required, StringLength(100)] string Name,
    [Required, EmailAddress, StringLength(200)] string Email,
    int? DepartmentId,
    int? ManagerId,
    List<string>? Roles);

public record DepartmentRequest([Required, StringLength(100)] string Name, int? HeadUserId);

public record CreateRoleRequest([Required, StringLength(50)] string Name);

public record AdminUserDto(
    int Id, string Name, string Email, int? DepartmentId, string? Department, int? ManagerId, string? Manager, List<string> Roles)
{
    public static AdminUserDto From(User u) => new(
        u.Id, u.Name, u.Email, u.DepartmentId, u.Department?.Name, u.ManagerId, u.Manager?.Name,
        u.Roles.Select(r => r.Name).OrderBy(n => n).ToList());
}

public record AdminDepartmentDto(int Id, string Name, int? HeadUserId, string? Head)
{
    public static AdminDepartmentDto From(Department d) => new(d.Id, d.Name, d.HeadUserId, d.HeadUser?.Name);
}
