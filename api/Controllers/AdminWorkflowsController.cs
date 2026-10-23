using ExpenseWorkflow.Api.Dtos;
using ExpenseWorkflow.Api.Engine;
using ExpenseWorkflow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseWorkflow.Api.Controllers;

[ApiController]
[RequireAdmin]
[Route("api/admin/workflows")]
public class AdminWorkflowsController(WorkflowAdminService workflows) : ControllerBase
{
    [HttpGet]
    public async Task<List<WorkflowDto>> List()
    {
        var currentId = await workflows.CurrentIdAsync();
        return (await workflows.ListAsync()).Select(d => WorkflowDto.From(d, d.Id == currentId)).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<WorkflowDto> Get(int id) => await Dto(await workflows.GetAsync(id));

    /// <summary>Starts a new draft (version 1, or the next version of an existing name).</summary>
    [HttpPost]
    public async Task<ActionResult<WorkflowDto>> Create(CreateWorkflowRequest request)
    {
        var draft = await workflows.CreateDraftAsync(request.Name, request.Rules);
        return CreatedAtAction(nameof(Get), new { id = draft.Id }, await Dto(draft));
    }

    /// <summary>Copies a version into a new draft so published rules can change without touching in-flight reports.</summary>
    [HttpPost("{id:int}/new-version")]
    public async Task<ActionResult<WorkflowDto>> NewVersion(int id)
    {
        var draft = await workflows.NewVersionAsync(id);
        return CreatedAtAction(nameof(Get), new { id = draft.Id }, await Dto(draft));
    }

    /// <summary>Replaces all rules of a draft. The order of rules is their evaluation order.</summary>
    [HttpPut("{id:int}")]
    public async Task<WorkflowDto> Save(int id, SaveRulesRequest request) =>
        await Dto(await workflows.SaveRulesAsync(id, request.Rules));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await workflows.DeleteDraftAsync(id);
        return NoContent();
    }

    [HttpPost("{id:int}/validate")]
    public async Task<ValidationDto> Validate(int id)
    {
        var result = await workflows.ValidateAsync(id);
        return new ValidationDto(result.Errors, result.Warnings);
    }

    /// <summary>Publishes a valid draft, now or from a future date. Responds 422 with the list of problems if invalid.</summary>
    [HttpPost("{id:int}/publish")]
    public async Task<WorkflowDto> Publish(int id, PublishRequest? request) =>
        await Dto(await workflows.PublishAsync(id, request?.EffectiveFrom));

    /// <summary>Shows the rule and approvers a report like this would get, without creating anything. Works on drafts.</summary>
    [HttpPost("{id:int}/simulate")]
    public async Task<SimulationDto> Simulate(int id, SimulateRequest request) => await workflows.SimulateAsync(id, request);

    private async Task<WorkflowDto> Dto(Models.WorkflowDefinition d) => WorkflowDto.From(d, d.Id == await workflows.CurrentIdAsync());
}
