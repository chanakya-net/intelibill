using Intelibill.Domain.Entities;
using Intelibill.Domain.Events;

namespace Intelibill.Domain.Unit.Tests.Common;

public class BaseEntityTests
{
    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        Assert.NotEmpty(user.DomainEvents);

        user.ClearDomainEvents();

        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void CreateWithEmail_RaisesUserRegisteredEvent()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");

        var evt = Assert.IsType<UserRegisteredEvent>(Assert.Single(user.DomainEvents));
        Assert.Equal(user.Id, evt.UserId);
        Assert.Equal("user@test.com", evt.Email);
    }
}