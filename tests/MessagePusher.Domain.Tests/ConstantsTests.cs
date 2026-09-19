using MessagePusher.Domain;

namespace MessagePusher.Domain.Tests;

public class ConstantsTests
{
    [Fact]
    public void Roles_match_go()
    {
        Assert.Equal(0, Roles.Guest);
        Assert.Equal(1, Roles.Common);
        Assert.Equal(10, Roles.Admin);
        Assert.Equal(100, Roles.Root);
    }
}
