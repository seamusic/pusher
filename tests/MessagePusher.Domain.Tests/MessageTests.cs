using MessagePusher.Domain.Entities;

namespace MessagePusher.Domain.Tests;

public class MessageTests
{
    [Fact]
    public void Clone_copies_all_fields_and_is_independent()
    {
        var original = new Message
        {
            Id = 7,
            UserId = 3,
            Title = "T",
            Description = "D",
            Content = "C",
            Url = "U",
            Channel = "ch",
            Timestamp = 123,
            Link = "lk",
            To = "to",
            Status = 1,
            RenderMode = "code",
            Token = "tk",
            HtmlContent = "<p>",
            Async = true,
            OpenId = "oid",
            Desp = "desp",
            Short = "short"
        };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.UserId, clone.UserId);
        Assert.Equal(original.Title, clone.Title);
        Assert.Equal(original.Description, clone.Description);
        Assert.Equal(original.Content, clone.Content);
        Assert.Equal(original.Url, clone.Url);
        Assert.Equal(original.Channel, clone.Channel);
        Assert.Equal(original.Timestamp, clone.Timestamp);
        Assert.Equal(original.Link, clone.Link);
        Assert.Equal(original.To, clone.To);
        Assert.Equal(original.Status, clone.Status);
        Assert.Equal(original.RenderMode, clone.RenderMode);
        Assert.Equal(original.Token, clone.Token);
        Assert.Equal(original.HtmlContent, clone.HtmlContent);
        Assert.Equal(original.Async, clone.Async);
        Assert.Equal(original.OpenId, clone.OpenId);
        Assert.Equal(original.Desp, clone.Desp);
        Assert.Equal(original.Short, clone.Short);

        clone.To = "changed";
        clone.Channel = "changed";
        clone.Title = "changed";
        clone.Content = "changed";
        Assert.Equal("to", original.To);
        Assert.Equal("ch", original.Channel);
        Assert.Equal("T", original.Title);
        Assert.Equal("C", original.Content);
    }
}
