using System.Text.Json.Serialization;

namespace MessagePusher.Application.Results;

public sealed class ApiResult<T>
{
    [JsonPropertyName("success")]
    public bool IsSuccess { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    public static ApiResult<T> Ok(T? data = default) => new() { IsSuccess = true, Data = data };

    public static ApiResult<T> Fail(string msg) => new() { IsSuccess = false, Message = msg };
}

public sealed class ApiResult
{
    [JsonPropertyName("success")]
    public bool IsSuccess { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    public static ApiResult Ok(object? data = null) => new() { IsSuccess = true, Data = data };

    public static ApiResult Fail(string msg) => new() { IsSuccess = false, Message = msg };
}

public sealed class PushResult
{
    [JsonPropertyName("success")]
    public bool IsSuccess { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    public static PushResult Ok(string uuid) => new() { IsSuccess = true, Uuid = uuid };

    public static PushResult Fail(string msg) => new() { IsSuccess = false, Message = msg };
}

public sealed class MessageStatusResult
{
    [JsonPropertyName("success")]
    public bool IsSuccess { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("status")]
    public int Status { get; set; }

    public static MessageStatusResult Ok(int status) => new() { IsSuccess = true, Status = status };

    public static MessageStatusResult Fail(string msg) => new() { IsSuccess = false, Message = msg };
}
