using VirtualDeskManager.Core.Configuration;
using VirtualDeskManager.Core.Updates;
using Xunit;

namespace VirtualDeskManager.Core.Tests;

public class AutoUpdatePolicyTests
{
    [Fact]
    public void It_restarts_only_when_the_user_is_away()
    {
        Assert.True(AutoUpdatePolicy.CanRestartNow(AutoUpdatePolicy.IdleNeeded));
        Assert.True(AutoUpdatePolicy.CanRestartNow(TimeSpan.FromHours(2)));

        Assert.False(AutoUpdatePolicy.CanRestartNow(AutoUpdatePolicy.IdleNeeded - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Installing_on_its_own_is_off_unless_the_user_turns_it_on()
    {
        UpdateSettings nuevos = new();

        Assert.False(nuevos.InstallAutomatically);
        Assert.Equal("", nuevos.InstalledAutomatically);
    }
}
