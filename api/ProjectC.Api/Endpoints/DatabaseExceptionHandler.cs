using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ProjectC.Api.Endpoints;

// A concurrent delete must not turn a normal client race into an unhandled 500.
public sealed class DatabaseExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception error, CancellationToken ct)
    {
        if (error is not DbUpdateConcurrencyException) return false;
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 409, Title = "데이터가 다른 요청에서 변경되었습니다. 다시 조회해 주세요."
        }, ct);
        return true;
    }
}
