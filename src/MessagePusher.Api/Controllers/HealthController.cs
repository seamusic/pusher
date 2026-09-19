using MessagePusher.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MessagePusher.Api.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Health() => Ok(new { status = "ok" });

    [HttpGet("/ready")]
    public async Task<IActionResult> Ready([FromServices] AppDbContext db, CancellationToken ct)
    {
        try
        {
            await db.Database.CanConnectAsync(ct);
            return Ok(new { status = "ready" });
        }
        catch
        {
            return StatusCode(503, new { status = "not ready" });
        }
    }
}
