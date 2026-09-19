using MessagePusher.Domain.Entities;
using MessagePusher.Domain.Exceptions;

namespace MessagePusher.Application.Channels;

// 每个新增 Type/模式提供独立校验器：检查必填项、目标、模式、URL、参数取值与互斥关系。
// 历史 Other 为标量/模板/复合字符串的通道不强制改为 JSON。
public interface IChannelConfigValidator
{
    string Type { get; }
    void Validate(Channel channel);
}

public sealed class ChannelConfigValidatorRegistry
{
    private readonly IReadOnlyDictionary<string, IChannelConfigValidator> _map;

    public ChannelConfigValidatorRegistry(IEnumerable<IChannelConfigValidator> validators) =>
        _map = validators.ToDictionary(v => v.Type, StringComparer.Ordinal);

    public IChannelConfigValidator? Find(string type) =>
        _map.TryGetValue(type, out var v) ? v : null;
}

public static class ChannelTargetPolicy
{
    // 动态目标通道：To 优先于配置 AccountId。
    public static string ResolveDynamic(Message message, Channel channel) =>
        string.IsNullOrEmpty(message.To) ? channel.AccountId : message.To;

    // 固定目标通道：凭证已绑定送达目标，非空 To 返回明确业务错误，不静默忽略、不当作发送密钥。
    public static void RejectFixedTargetTo(Message message, string channelLabel)
    {
        if (!string.IsNullOrEmpty(message.To))
            throw new BusinessException($"{channelLabel}为固定目标通道，不支持指定发送目标；请清空 To（群组中该子通道目标应留空）");
    }
}
