using Sfc.Domain.Common;
using Xunit;

namespace Sfc.Domain.Tests.Common;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("João Peixão", "joao-peixao")]
    [InlineData("Ötzi Müller", "otzi-muller")]
    [InlineData("K1 Fighter!!!", "k1-fighter")]
    [InlineData("  Multiple   Spaces  ", "multiple-spaces")]
    [InlineData("UPPER-case", "upper-case")]
    [InlineData("a--b---c", "a-b-c")]
    public void Generate_ProducesLowercaseAsciiHyphenatedSlug(string input, string expected)
    {
        Assert.Equal(expected, SlugGenerator.Generate(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Generate_WithBlankInput_Throws(string? input)
    {
        Assert.Throws<ArgumentException>(() => SlugGenerator.Generate(input!));
    }

    [Fact]
    public void Generate_WithNoAlphanumericCharacters_Throws()
    {
        Assert.Throws<ArgumentException>(() => SlugGenerator.Generate("!!! ---"));
    }

    [Fact]
    public void Generate_IsIdempotentOnItsOwnOutput()
    {
        var slug = SlugGenerator.Generate("João Peixão");
        Assert.Equal(slug, SlugGenerator.Generate(slug));
    }
}
