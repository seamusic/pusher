namespace MessagePusher.Domain;

public enum UserStatus
{
    NonExisted = 0,
    Enabled = 1,
    Disabled = 2
}

public enum MessageSendStatus
{
    Unknown = 0,
    Pending = 1,
    Sent = 2,
    Failed = 3,
    AsyncPending = 4
}

public enum ChannelStatus
{
    Unknown = 0,
    Enabled = 1,
    Disabled = 2
}

public enum WebhookStatus
{
    Unknown = 0,
    Enabled = 1,
    Disabled = 2
}
