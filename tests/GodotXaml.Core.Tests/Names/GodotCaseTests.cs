using GodotXaml.Core.Names;

namespace GodotXaml.Core.Tests.Names;

public sealed class GodotCaseTests
{
    // Godot 4.7.2's ucaps.h: LTU_LEN 1505, UTL_LEN 1488.
    [Fact]
    public void TheTablesHaveUcapsLengths()
    {
        Assert.Equal(1505, GodotCaseTables.UpperFrom.Length);
        Assert.Equal(1505, GodotCaseTables.UpperTo.Length);
        Assert.Equal(1488, GodotCaseTables.LowerFrom.Length);
        Assert.Equal(1488, GodotCaseTables.LowerTo.Length);
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

    private static void AssertStrictlyAscending(ReadOnlySpan<int> keys)
    {
        for (int i = 1; i < keys.Length; i++)
        {
            Assert.True(keys[i - 1] < keys[i], $"Index {i}: 0x{keys[i]:X4} follows 0x{keys[i - 1]:X4}.");
        }
    }
}
