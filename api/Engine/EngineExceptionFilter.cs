using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ExpenseWorkflow.Api.Engine;

/// <summary>Maps engine errors to ProblemDetails responses.</summary>
public class EngineExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case WorkflowValidationException v:
                context.Result = new UnprocessableEntityObjectResult(new
                {
                    status = 422,
                    detail = v.Message,
                    errors = v.Result.Errors,
                    warnings = v.Result.Warnings
                });
                break;
            case EngineException e:
                var status = e.Kind switch
                {
                    EngineError.NotFound => StatusCodes.Status404NotFound,
                    EngineError.Forbidden => StatusCodes.Status403Forbidden,
                    EngineError.Conflict => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status422UnprocessableEntity
                };
                context.Result = new ObjectResult(new ProblemDetails { Status = status, Detail = e.Message }) { StatusCode = status };
                break;
            default:
                return;
        }
        context.ExceptionHandled = true;
    }
}
