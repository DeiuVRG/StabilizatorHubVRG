using StabilizatorHub.Infrastructure.Security;
using Xunit;

namespace StabilizatorHub.Tests;

public class PairingCodeHasherTests
{
    private readonly PairingCodeHasher _hasher = new();

    [Fact]
    public void Hash_RoundTripsWithVerify()
    {
        var hash = _hasher.Hash("7F3K9Q");

        Assert.True(_hasher.Verify("7F3K9Q", hash));
        Assert.False(_hasher.Verify("7F3K9X", hash));
    }

    [Fact]
    public void Hash_IsSalted_SoTwoHashesOfTheSameCodeDiffer()
    {
        Assert.NotEqual(_hasher.Hash("7F3K9Q"), _hasher.Hash("7F3K9Q"));
    }

    [Fact]
    public void Hash_NeverContainsTheCodeInClear()
    {
        Assert.DoesNotContain("7F3K9Q", _hasher.Hash("7F3K9Q"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("PBKDF2.notanumber.AAAA.BBBB")]
    [InlineData("PBKDF2.100000.!!!.???")]
    public void Verify_RejectsMalformedStoredHashes(string storedHash)
    {
        Assert.False(_hasher.Verify("7F3K9Q", storedHash));
    }
}
