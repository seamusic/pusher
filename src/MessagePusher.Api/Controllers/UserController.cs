using MessagePusher.Api.Auth;
using MessagePusher.Application.Results;
using MessagePusher.Application.Services;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;

namespace MessagePusher.Api.Controllers;

[ApiController]
[Route("api/user")]
public sealed class UserController : ControllerBase
{
    private readonly UserService _users;
    public UserController(UserService users) => _users = users;

    [HttpGet("self")]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> Self(CancellationToken ct) =>
        Ok(ApiResult<object>.Ok(await _users.GetSelfAsync(CurrentUser.Id(HttpContext), ct)));

    [HttpPut("self")]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> UpdateSelf([FromBody] User user, CancellationToken ct)
    {
        await _users.UpdateSelfAsync(CurrentUser.Id(HttpContext), user, ct);
        return Ok(ApiResult.Ok());
    }

    [HttpDelete("self")]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> DeleteSelf(CancellationToken ct)
    {
        await _users.DeleteSelfAsync(CurrentUser.Id(HttpContext), ct);
        return Ok(ApiResult.Ok());
    }

    [HttpGet("token")]
    [RequireRole(Roles.Common)]
    public async Task<IActionResult> Token(CancellationToken ct)
    {
        var token = await _users.GenerateTokenAsync(CurrentUser.Id(HttpContext), ct);
        return Ok(ApiResult<string>.Ok(token));
    }

    [HttpGet("")]
    [RequireRole(Roles.Admin)]
    public async Task<IActionResult> List([FromQuery] int p, CancellationToken ct)
    {
        if (p < 0) p = 0;
        return Ok(ApiResult<object>.Ok(await _users.ListAsync(p, ct)));
    }

    [HttpGet("search")]
    [RequireRole(Roles.Admin)]
    public async Task<IActionResult> Search([FromQuery] string keyword, CancellationToken ct) =>
        Ok(ApiResult<object>.Ok(await _users.SearchAsync(keyword ?? "", ct)));

    [HttpGet("{id:int}")]
    [RequireRole(Roles.Admin)]
    public async Task<IActionResult> Get(int id, CancellationToken ct) =>
        Ok(ApiResult<object>.Ok(await _users.GetAsync(id, CurrentUser.Role(HttpContext), ct)));

    [HttpPost("")]
    [RequireRole(Roles.Admin)]
    public async Task<IActionResult> Create([FromBody] User user, CancellationToken ct)
    {
        await _users.CreateAsync(user, CurrentUser.Role(HttpContext), ct);
        return Ok(ApiResult.Ok());
    }

    [HttpPost("manage")]
    [RequireRole(Roles.Admin)]
    public async Task<IActionResult> Manage([FromBody] ManageRequest req, CancellationToken ct)
    {
        var data = await _users.ManageAsync(req.Username, req.Action, CurrentUser.Role(HttpContext), ct);
        return Ok(ApiResult<object>.Ok(data));
    }

    [HttpPut("")]
    [RequireRole(Roles.Admin)]
    public async Task<IActionResult> Update([FromBody] User user, CancellationToken ct)
    {
        await _users.UpdateUserAsync(user, CurrentUser.Role(HttpContext), ct);
        return Ok(ApiResult.Ok());
    }

    [HttpDelete("{id:int}")]
    [RequireRole(Roles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _users.DeleteAsync(id, CurrentUser.Role(HttpContext), ct);
        return Ok(ApiResult.Ok());
    }

    public sealed class ManageRequest
    {
        public string Username { get; set; } = "";
        public string Action { get; set; } = "";
    }
}
