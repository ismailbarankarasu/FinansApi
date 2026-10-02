using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FinansApi.Infrastructure.Errors;

public sealed class ProblemResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { StatusCode: >= 400 } result)
        {
            var status = result.StatusCode!.Value;
            var problem = result.Value as ProblemDetails;
            if (problem is null)
            {
                var message = result.Value?.GetType().GetProperty("message")?.GetValue(result.Value)?.ToString() ?? "İstek işlenemedi.";
                problem = new ProblemDetails
                {
                    Status = status,
                    Title = message,
                    Detail = message

                };
                problem.Extensions["message"] = message;
            }

            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            context.Result = new ObjectResult(problem)
            {
                StatusCode = status
            };
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

}
