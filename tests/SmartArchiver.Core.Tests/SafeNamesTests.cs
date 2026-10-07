using SmartArchiver.Core;

namespace SmartArchiver.Core.Tests;

public class SafeNamesTests
{
    [Theory]
    [InlineData("a.txt")]
    [InlineData("файл.txt")]
    [InlineData("report v2.pdf")]
    [InlineData(".hidden")]
    [InlineData("a..b")]
    [InlineData("name_without_extension")]
    public void ValidNames_AreSafe(string name)
    {
        Assert.True(SafeNames.IsSafe(name, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:file")]
    [InlineData("a:b")]
    [InlineData("a\0b")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("../x")]
    [InlineData("..\\x")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share")]
    [InlineData("C:\\Windows\\system32")]
    public void DangerousNames_AreRejected(string name)
    {
        Assert.False(SafeNames.IsSafe(name, out string reason));
        Assert.NotEmpty(reason);
        TestData.AssertCode(ErrorCode.E_PAYLOAD, () => SafeNames.EnsureSafe(name));
    }

    [Fact]
    public void NullName_IsRejected()
    {
        Assert.False(SafeNames.IsSafe(null, out _));
    }

    [Fact]
    public void LengthLimit_Is255BytesOfUtf8()
    {
        Assert.True(SafeNames.IsSafe(new string('a', 255), out _));
        Assert.False(SafeNames.IsSafe(new string('a', 256), out _));
        // «я» займає 2 байти: 127 символів = 254 байти, 128 символів = 256 байтів.
        Assert.True(SafeNames.IsSafe(new string('я', 127), out _));
        Assert.False(SafeNames.IsSafe(new string('я', 128), out _));
    }

    [Fact]
    public void LoneSurrogate_IsRejected()
    {
        Assert.False(SafeNames.IsSafe("bad\uD800name", out _));
    }
}
