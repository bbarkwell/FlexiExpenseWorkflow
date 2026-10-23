using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ExpenseWorkflow.Api.Services;

/// <summary>Allows only users holding the Admin role. Swap for a real authorization policy once authentication exists.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAdminAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = await context.HttpContext.RequestServices.GetRequiredService<CurrentUser>().GetAsync();
        if (user is null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        if (!user.Roles.Any(r => r.Name == "Admin"))
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 403, Detail = "The Admin role is required." }) { StatusCode = 403 };
            return;
        }
        await next();
    }
}
