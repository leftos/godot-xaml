using System.Globalization;
using GodotXaml.Core.Values;

namespace GodotXaml.Core.Tests.Values;

public sealed class GodotFloatFormatTests
{
    // The float text of rtos_fix (variant_parser.cpp:1986-2000), then grisu2's format_buffer
    // (thirdparty/grisu2/grisu2.h:841-896) and the top-level ".0" (variant_parser.cpp:2023-2031).
    [Theory]
    [InlineData(0.0, "0", "0.0")]
    [InlineData(1.0, "1", "1.0")]
    [InlineData(-1.0, "-1", "-1.0")]
    [InlineData(0.1, "0.1", "0.1")]
    [InlineData((double)0.1f, "0.1", "0.1")]
    [InlineData(1.5, "1.5", "1.5")]
    [InlineData(1e14, "100000000000000", "100000000000000.0")]
    [InlineData(1e15, "1e+15", "1e+15")]
    [InlineData(1e20, "1e+20", "1e+20")]
    [InlineData(1e21, "1e+21", "1e+21")]
    [InlineData(1e-4, "0.0001", "0.0001")]
    [InlineData(1e-5, "1e-05", "1e-05")]
    [InlineData(1e-7, "1e-07", "1e-07")]
    [InlineData(1.5e-5, "1.5e-05", "1.5e-05")]
    [InlineData(123456789.0, "123456789", "123456789.0")]
    [InlineData(123456789012345678.0, "1.2345678901234568e+17", "1.2345678901234568e+17")]
    [InlineData((double)float.MaxValue, "3.4028235e+38", "3.4028235e+38")]
    [InlineData((double)(1f / 3f), "0.33333334", "0.33333334")]
    [InlineData(1.0 / 3.0, "0.3333333333333333", "0.3333333333333333")]
    [InlineData(0.1 + 0.2, "0.30000000000000004", "0.30000000000000004")]
    [InlineData(9007199254740992.0, "9.007199e+15", "9.007199e+15")]
    [InlineData(9223372036854775808.0, "9.223372e+18", "9.223372e+18")]
    [InlineData((double)16777217f, "16777216", "16777216.0")]
    [InlineData(1e300, "1e+300", "1e+300")]
    [InlineData(double.MaxValue, "1.7976931348623157e+308", "1.7976931348623157e+308")]
    [InlineData(5e-324, "5e-324", "5e-324")]
    [InlineData((double)float.Epsilon, "1e-45", "1e-45")]
    [InlineData(double.PositiveInfinity, "inf", "inf")]
    [InlineData(double.NegativeInfinity, "inf_neg", "inf_neg")]
    [InlineData(double.NaN, "nan", "nan")]
    public void FormatsTheMapTable(double value, string component, string topLevel)
    {
        Assert.Equal(component, GodotFloatFormat.Format(value, compat: true));
        Assert.Equal(topLevel, GodotFloatFormat.FormatTopLevel(value, compat: true));
    }

    // variant_parser.cpp:1989-1993: inf_neg only in compat; otherwise String::num gives "-inf" (ustring.cpp:1411-1414).
    [Fact]
    public void WritesNegativeInfinityAsMinusInfOutsideCompat()
    {
        Assert.Equal("-inf", GodotFloatFormat.Format(double.NegativeInfinity, compat: false));
        Assert.Equal("-inf", GodotFloatFormat.FormatTopLevel(double.NegativeInfinity, compat: false));
    }

    // variant_parser.cpp:1987: zero of either sign is written "0" (xUnit treats a -0.0 data row as a duplicate of 0.0).
    [Fact]
    public void WritesNegativeZeroAsZero()
    {
        Assert.Equal("0", GodotFloatFormat.Format(-0.0, compat: true));
        Assert.Equal("0.0", GodotFloatFormat.FormatTopLevel(-0.0, compat: true));
    }

    [Fact]
    public void MatchesGodotOnTheDoubleCorpus() =>
        AssertCorpus("doubles.txt", hex => GodotFloatFormat.FormatTopLevel(BitConverter.Int64BitsToDouble(ParseBits(hex)), compat: true));

    [Fact]
    public void MatchesGodotOnTheFloat32Corpus() =>
        AssertCorpus("float32.txt", hex => GodotFloatFormat.Format(BitConverter.Int32BitsToSingle((int)ParseBits(hex)), compat: true));

    private static long ParseBits(string hex) => (long)ulong.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

    private static void AssertCorpus(string fileName, Func<string, string> format)
    {
        string[] lines = File.ReadAllText(RepoFiles.PathOf("tests", "fixtures", "values", "floats", fileName)).Split('\n');
        var mismatches = new List<string>();
        foreach (string line in lines)
        {
            string[] fields = line.Split('\t');
            string actual = format(fields[0]);
            if (actual != fields[1])
            {
                mismatches.Add($"{fields[0]}: Godot {fields[1]}, ours {actual}");
            }
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} of {lines.Length} lines differ:\n{string.Join('\n', mismatches.Take(20))}");
        Assert.True(lines.Length > 5000, $"{fileName} has {lines.Length} lines");
    }
}
