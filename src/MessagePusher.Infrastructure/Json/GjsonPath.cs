using System.Text.Json;
using MessagePusher.Application.Abstractions;

namespace MessagePusher.Infrastructure.Json;

public sealed class GjsonPath : IGjson
{
    public string GetString(string json, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement current = doc.RootElement;
            foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "#")
                {
                    if (current.ValueKind == JsonValueKind.Array && current.GetArrayLength() > 0)
                        current = current[0];
                    else
                        return "";
                    continue;
                }
                if (int.TryParse(part, out var idx) && current.ValueKind == JsonValueKind.Array)
                {
                    if (idx < 0 || idx >= current.GetArrayLength())
                        return "";
                    current = current[idx];
                    continue;
                }
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                    return "";
            }
            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString() ?? "",
                JsonValueKind.Number => current.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "",
                _ => current.GetRawText()
            };
        }
        catch
        {
            return "";
        }
    }
}
