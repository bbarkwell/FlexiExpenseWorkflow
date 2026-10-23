using ExpenseWorkflow.Api.Dtos;
using ExpenseWorkflow.Api.Engine;
using ExpenseWorkflow.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseWorkflow.Api.Controllers;

/// <summary>My own out-of-office windows. While one is active, my approvals go to the delegate.</summary>
[ApiController]
[Route("api/me/delegations")]
public class MyDelegationsController(CurrentUser currentUser, DelegationManager delegations) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<DelegationDto>>> List()
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        return (await delegations.ListAsync(me.Id)).Select(DelegationDto.From).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<DelegationDto>> Create(CreateDelegationRequest request)
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        var created = await delegations.CreateAsync(me, request.DelegateUserId, request.StartsOn, request.EndsOn);
        return CreatedAtAction(nameof(List), DelegationDto.From(created));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await currentUser.GetAsync() is not { } me) return Unauthorized();
        await delegations.RemoveAsync(me, id);
        return NoContent();
    }
}
