using CCL.MES.Shared.Hardware;

namespace CCL.MES.Hybrid.Client.Tests.Hardware;

public sealed class HardwareAvailabilityTests
{
    [Fact]
    public void Ok_returns_available_with_null_message()
    {
        var a = HardwareAvailability.Ok();
        Assert.True(a.IsAvailable);
        Assert.Equal("ok", a.Reason);
        Assert.Null(a.OperatorMessage);
    }

    [Fact]
    public void FeatureDisabled_returns_not_available_with_operator_message()
    {
        var a = HardwareAvailability.FeatureDisabled();
        Assert.False(a.IsAvailable);
        Assert.Equal("feature_disabled", a.Reason);
        Assert.NotNull(a.OperatorMessage);
        Assert.Contains("tắt", a.OperatorMessage); // Vietnamese "off"
    }

    [Fact]
    public void NotImplemented_carries_component_name_into_message()
    {
        var a = HardwareAvailability.NotImplemented("Cân");
        Assert.False(a.IsAvailable);
        Assert.Equal("not_implemented", a.Reason);
        Assert.Contains("Cân", a.OperatorMessage!);
    }

    [Fact]
    public void PermissionDenied_carries_settings_hint()
    {
        var a = HardwareAvailability.PermissionDenied("camera");
        Assert.False(a.IsAvailable);
        Assert.Equal("permission_denied", a.Reason);
        Assert.Contains("Privacy", a.OperatorMessage!);
        Assert.Contains("System Settings", a.OperatorMessage!);
    }

    [Theory]
    [InlineData("not_implemented", true)]
    [InlineData("feature_disabled", true)]
    [InlineData("permission_denied", true)]
    [InlineData("no_device", true)]
    [InlineData("busy", true)]
    [InlineData("platform_unsupported", true)]
    [InlineData("ok", false)]  // ok is paired with IsAvailable=true so no message
    public void Factory_methods_always_pair_unavailable_with_non_null_message(string reason, bool expectMessage)
    {
        HardwareAvailability a = reason switch
        {
            "not_implemented" => HardwareAvailability.NotImplemented("X"),
            "feature_disabled" => HardwareAvailability.FeatureDisabled(),
            "permission_denied" => HardwareAvailability.PermissionDenied("X"),
            "no_device" => HardwareAvailability.NoDevice("X"),
            "busy" => HardwareAvailability.Busy("X"),
            "platform_unsupported" => HardwareAvailability.PlatformUnsupported("X"),
            "ok" => HardwareAvailability.Ok(),
            _ => throw new InvalidOperationException()
        };
        if (expectMessage)
        {
            Assert.False(a.IsAvailable);
            Assert.NotNull(a.OperatorMessage);
            Assert.NotEmpty(a.OperatorMessage);
        }
        else
        {
            Assert.True(a.IsAvailable);
            Assert.Null(a.OperatorMessage);
        }
    }
}
