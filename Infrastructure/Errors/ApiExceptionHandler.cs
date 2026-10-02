using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinansApi.Infrastructure.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var domain = exception as DomainException;
        var conflict = exception is DbUpdateConcurrencyException
        || exception is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 or 5 or 6 } }
        || exception is SqliteException { SqliteErrorCode: 5 or 6 };
        var status = domain?.Status ?? (conflict ? 409 : 500);
        var message = domain?.Message ?? (conflict ? "Kayıt başka bir işlemle çakıştı. Veriyi yenileyip tekrar deneyin." : "Sunucu hatası.");
        if (status == 500)
        {
            logger.LogError(exception, "İşlenmeyen API hatası");
        }

        ProblemDetails problem = domain?.Field is { } field ? new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }) : new ProblemDetails();
        problem.Status = status;
        problem.Title = message;
        problem.Detail = message;
        problem.Extensions["message"] = message;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

}
