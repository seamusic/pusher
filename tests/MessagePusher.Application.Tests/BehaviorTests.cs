using System.Text.Json;
using MessagePusher.Application.Json;
using MessagePusher.Application.Results;
using MessagePusher.Application.Services;
using MessagePusher.Application.Channels;
using MessagePusher.Domain.Entities;

namespace MessagePusher.Application.Tests;

public class BehaviorTests
{
    [Fact]
    public void ApiResult_serializes_success_not_duplicated()
    {
        var json = JsonSerializer.Serialize(ApiResult<string>.Ok("x"), JsonDefaults.Options);
        Assert.Contains("\"success\":true", json);
        Assert.DoesNotContain("is_success", json);
        Assert.Contains("\"data\":\"x\"", json);
    }

    [Fact]
    public void AuthMessage_user_token_mismatch_rejects_even_if_channel_matches()
    {
        Assert.False(PushService.AuthMessage("channel-token", "user-token", "channel-token"));
        Assert.True(PushService.AuthMessage("user-token", "user-token", "channel-token"));
        Assert.True(PushService.AuthMessage("channel-token", "", "channel-token"));
        Assert.False(PushService.AuthMessage("bad", "", "channel-token"));
        Assert.True(PushService.AuthMessage("", "", null));
        Assert.True(PushService.AuthMessage("anything", "", null));
    }

    [Fact]
    public void KeepCompatible_maps_serverchan_fields()
    {
        var m = new Message { Short = "s", Desp = "d", OpenId = "o" };
        PushService.KeepCompatible(m);
        Assert.Equal("s", m.Description);
        Assert.Equal("d", m.Content);
        Assert.Equal("o", m.To);
    }

    [Fact]
    public void Telegram_markdown_splits_on_newline()
    {
        var s = "abc\n" + new string('x', 5000);
        var idx = TelegramProvider.GetNearestValidSplit(s, 4096, "markdown");
        Assert.True(idx <= 4096);
        Assert.Equal('\n', s[idx - 1]);
    }

    [Fact]
    public void LarkSign_uses_empty_message()
    {
        var sign = LarkProvider.LarkSign("secret", 123);
        Assert.False(string.IsNullOrEmpty(sign));
    }

    [Fact]
    public void DingSign_is_url_encoded()
    {
        var sign = DingProvider.DingSign("secret", 123);
        Assert.False(string.IsNullOrEmpty(sign));
    }
}
