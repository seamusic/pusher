using MessagePusher.Api.Auth;
using MessagePusher.Application.Results;
using MessagePusher.Application.Services;
using MessagePusher.Domain;
using MessagePusher.Domain.Entities;

namespace MessagePusher.Api.Controllers;

[ApiController]
[Route("api/option")]
[RequireRole(Roles.Root)]
public sealed class OptionController : ControllerBase
{
    private readonly OptionService _options;
    public OptionController(OptionService options) => _options = options;

    [HttpGet("")]
    public IActionResult Get() => Ok(ApiResult<object>.Ok(_options.GetPublic()));

    [HttpPut("")]
    public async Task<IActionResult> Update([FromBody] Option option, CancellationToken ct)
    {
        await _options.UpdateAsync(option.Key, option.Value, ct);
        return Ok(ApiResult.Ok());
    }
}
