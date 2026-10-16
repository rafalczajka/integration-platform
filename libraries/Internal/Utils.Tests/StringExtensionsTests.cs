using System;
using Xunit;

namespace Integrations.Utils.Tests;

public sealed class StringExtensionsTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("HomeOffice", "home-office")]
    [InlineData("HTTPServer", "http-server")]
    [InlineData("XMLHttpRequest", "xml-http-request")]
    [InlineData("HTTP", "http")]
    [InlineData("Version2Update", "version2-update")]
    [InlineData("already-kebab-case", "already-kebab-case")]
    [InlineData("Remont \u0141azienki", "remont-\u0142azienki")]
    public void ToKebabCase_ConvertsWordsAndPreservesUnicode(string input, string expected)
    {
        Assert.Equal(expected, input.ToKebabCase());
        Assert.Equal(expected, expected.ToKebabCase());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("  -- + /  ", "")]
    [InlineData("HomeOffice", "home-office")]
    [InlineData("HTTPServer", "http-server")]
    [InlineData("Version2Update", "version2-update")]
    [InlineData("  Home__Office / Test + Done! ", "home-office-test-done")]
    [InlineData("Remont \u0141azienki", "remont-lazienki")]
    [InlineData("\u0141\u00f3d\u017a", "lodz")]
    [InlineData("Caf\u00e9Cr\u00e8me", "cafe-creme")]
    [InlineData("Cafe\u0301Cre\u0300me", "cafe-creme")]
    [InlineData("Hello\U0001f600World", "hello-world")]
    public void ToAsciiSlug_RemovesDiacriticsAndRestrictsAlphabet(string input, string expected)
    {
        var actual = input.ToAsciiSlug();
        Assert.Equal(expected, actual);
        Assert.Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$|^$", actual);
        Assert.Equal(expected, actual.ToAsciiSlug());
    }

    [Fact]
    public void ToKebabCase_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StringExtensions.ToKebabCase(null!));
    }

    [Fact]
    public void ToAsciiSlug_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StringExtensions.ToAsciiSlug(null!));
    }
}
