using FMB.WebMVC.Services;

namespace FMB.Tests;

public sealed class PinCredentialTests
{
    [Fact]
    public void HashAndVerify_AcceptsOnlyTheMatchingPin()
    {
        var hash = PinCredential.Hash("4826");

        Assert.NotEqual("4826", hash);
        Assert.True(PinCredential.Verify("4826", hash));
        Assert.False(PinCredential.Verify("1234", hash));
        Assert.False(PinCredential.Verify("4826", "invalid"));
    }
}
