using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProjectC.Api.Auth;
using ProjectC.Api.Data;

namespace ProjectC.Api.Endpoints;

public static class RefreshEndpoints
{
    public static void MapRefreshEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/refresh", async (RefreshRequest request, RefreshTokenService tokens, CancellationToken ct) =>
        {
            if (request.RefreshToken is not { Length: 64 }) return Results.Unauthorized();
            var response = await tokens.Rotate(request.RefreshToken, ct);
            return response is null ? Results.Unauthorized() : Results.Ok(response);
        }).RequireRateLimiting("refresh");
        app.MapPost("/api/auth/revoke", async (RefreshRequest request, RefreshTokenService tokens, CancellationToken ct) =>
        {
            if (request.RefreshToken is not { Length: 64 }) return Results.Unauthorized();
            await tokens.Revoke(request.RefreshToken, ct);
            return Results.NoContent();
        }).RequireRateLimiting("refresh");
        // Allows existing, still valid guest JWTs to acquire account persistence.
        app.MapPost("/api/auth/session", async (ClaimsPrincipal user, GameDbContext db, RefreshTokenService tokens, CancellationToken ct) =>
        {
            var id = Guid.Parse(user.FindFirst("sub")!.Value);
            var player = await db.Players.SingleAsync(p => p.Id == id, ct);
            var response = tokens.Issue(player);
            await db.SaveChangesAsync(ct);
            return Results.Ok(response);
        }).RequireAuthorization().RequireRateLimiting("refresh");
    }
}
