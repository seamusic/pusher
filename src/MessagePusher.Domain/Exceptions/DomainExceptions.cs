namespace MessagePusher.Domain.Exceptions;

public class BusinessException : Exception
{
    public BusinessException(string message) : base(message)
    {
    }
}

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}

public class UnsupportedChannelException : BusinessException
{
    public UnsupportedChannelException(string type) : base("不支持的消息通道：" + type)
    {
    }
}
