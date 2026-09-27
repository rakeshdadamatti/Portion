using System.Text;
using Portion.Domain.Common;
using Portion.Domain.ValueObjects;

namespace Portion.Domain.Tests;

/// <summary>Covers the guard helpers and the content hash value object.</summary>
public class GuardAndHashTests
{
    [Fact]
    public void NotNull_ReturnsTheValue_WhenPresent()
    {
        Assert.Equal("value", Guard.Against.NotNull("value"));
    }

    [Fact]
    public void NotNull_Throws_WhenNull()
    {
        Assert.Throws<ArgumentNullException>(() => Guard.Against.NotNull((string?)null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NotNullOrWhiteSpace_RejectsBlankStrings(string? value)
    {
        Assert.Throws<ArgumentException>(() => Guard.Against.NotNullOrWhiteSpace(value));
    }

    [Fact]
    public void OutOfRange_RejectsValuesOutsideTheBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Guard.Against.OutOfRange(11, 1, 10));
    }

    [Fact]
    public void OutOfRange_AcceptsTheInclusiveBounds()
    {
        Assert.Equal(10, Guard.Against.OutOfRange(10, 1, 10));
        Assert.Equal(1, Guard.Against.OutOfRange(1, 1, 10));
    }

    [Fact]
    public void Hash_OfKnownContent_MatchesTheExpectedDigest()
    {
        var hash = FileHash.FromBytes(Encoding.UTF8.GetBytes("abc"));

        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", hash.Value);
        Assert.True(hash.IsValid);
    }

    [Fact]
    public void Hash_OfIdenticalContent_IsStable()
    {
        var first = FileHash.FromBytes(Encoding.UTF8.GetBytes("resume"));
        var second = FileHash.FromBytes(Encoding.UTF8.GetBytes("resume"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Hash_OfDifferentContent_Differs()
    {
        var first = FileHash.FromBytes(Encoding.UTF8.GetBytes("resume a"));
        var second = FileHash.FromBytes(Encoding.UTF8.GetBytes("resume b"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void FromString_NormalisesCase_SoHashesCompareReliably()
    {
        var lower = FileHash.FromString("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        var upper = FileHash.FromString("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");

        Assert.Equal(upper, lower);
    }

    [Fact]
    public async Task FromFile_MatchesTheInMemoryDigest_OfTheSameBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("Ada Lovelace\nAnalytical Engine\n");
        var path = Path.Combine(Path.GetTempPath(), $"portion-hash-{Guid.NewGuid():N}.txt");

        await File.WriteAllBytesAsync(path, bytes);

        try
        {
            Assert.Equal(FileHash.FromBytes(bytes), FileHash.FromFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
