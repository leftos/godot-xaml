using GodotXaml.Core.Names;

namespace GodotXaml.Core.Tests.Names;

public sealed class GodotCaseTests
{
    // Godot 4.8's ucaps.h, generated from Unicode 18.0.0: LTU_LEN 1525, UTL_LEN 1508.
    [Fact]
    public void TheTablesHaveUcapsLengths()
    {
        Assert.Equal(1525, GodotCaseTables.UpperFrom.Length);
        Assert.Equal(1525, GodotCaseTables.UpperTo.Length);
        Assert.Equal(1508, GodotCaseTables.LowerFrom.Length);
        Assert.Equal(1508, GodotCaseTables.LowerTo.Length);
    }

    [Fact]
    public void TheSearchedColumnsAreStrictlyAscending()
    {
        AssertStrictlyAscending(GodotCaseTables.UpperFrom);
        AssertStrictlyAscending(GodotCaseTables.LowerFrom);
    }

    [Theory]
    [InlineData(0x0131, 0x0049)] // Dotless i, which .NET's invariant casing leaves alone.
    [InlineData(0x10D0, 0x1C90)] // Georgian Mkhedruli to Mtavruli.
    [InlineData(0x10428, 0x10400)] // Deseret, beyond U+FFFF.
    [InlineData(0x0041, 0x0041)] // Already upper case.
    [InlineData(0x4E00, 0x4E00)] // Not in the table.
    public void FindUpperReturnsTheTablesUpperCase(int c, int expected) => Assert.Equal(expected, GodotCase.FindUpper(c));

    [Theory]
    [InlineData(0x0130, 0x0069)] // I with dot above, which .NET's invariant casing leaves alone.
    [InlineData(0x10400, 0x10428)] // Deseret, beyond U+FFFF.
    public void FindLowerReturnsTheTablesLowerCase(int c, int expected) => Assert.Equal(expected, GodotCase.FindLower(c));

    // A pair 4.8's ucaps.h adds with Unicode 18.0.0: caps_table gains { 0x0277, 0xA7DD } and reverse_caps_table
    // { 0xA7DD, 0x0277 }. Neither code point appears at all in 4.7.2's ucaps.h, so 4.7's tables answer 0x0277 here.
    [Fact]
    public void UpperCasesACodePointNewInUnicode18() => Assert.Equal(0xA7DD, GodotCase.FindUpper(0x0277));

    [Fact]
    public void LowerCasesACodePointNewInUnicode18() => Assert.Equal(0x0277, GodotCase.FindLower(0xA7DD));

    private static void AssertStrictlyAscending(ReadOnlySpan<int> keys)
    {
        for (int i = 1; i < keys.Length; i++)
        {
            Assert.True(keys[i - 1] < keys[i], $"Index {i}: 0x{keys[i]:X4} follows 0x{keys[i - 1]:X4}.");
        }
    }
}
