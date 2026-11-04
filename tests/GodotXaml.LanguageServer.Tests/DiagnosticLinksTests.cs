using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using GodotXaml.Core.Diagnostics;

namespace GodotXaml.LanguageServer.Tests;

public sealed partial class DiagnosticLinksTests
{
    private const string Page = "https://github.com/leftos/godot-xaml/blob/main/docs/format/diagnostics.md";

    [Theory]
    [InlineData("GX0001 Not well-formed XML", "gx0001-not-well-formed-xml")]
    [InlineData("GX0101 Unknown x: element", "gx0101-unknown-x-element")]
    [InlineData("GXC0006 Sub-resource cycle", "gxc0006-sub-resource-cycle")]
    public void AHeadingBecomesGitHubsAnchor(string heading, string anchor) => Assert.Equal(anchor, GitHubAnchor(heading));

    [Fact]
    public void EveryParserCodeLinksToItsHeading()
    {
        const string Reference = "docs/format/diagnostics.md";
        string full = RepoRoot.PathOf(Reference.Split('/'));
        Assert.True(File.Exists(full), $"{Reference} does not exist");
        var headings = File.ReadAllText(full)
            .Split('\n')
            .Select(line => Heading().Match(line.TrimEnd('\r')))
            .Where(match => match.Success)
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[1].Value + " " + match.Groups[2].Value, StringComparer.Ordinal);

        string[] codes =
        [
            .. typeof(XscnCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!)
                .Order(StringComparer.Ordinal),
        ];

        Assert.NotEmpty(codes);
        Assert.All(
            codes,
            code =>
            {
                Assert.True(headings.TryGetValue(code, out string? heading), $"{Reference} has no heading for {code}");
                Assert.Equal($"{Page}#{GitHubAnchor(heading)}", DiagnosticLinks.Href(code));
            }
        );
    }

    // GitHub's heading anchor: lowercase, every character but a letter, a digit, a space, a hyphen or an underscore
    // dropped, and each space a hyphen.
    private static string GitHubAnchor(string heading)
    {
        StringBuilder anchor = new();
        foreach (char c in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                anchor.Append(c);
            }
            else if (c == ' ')
            {
                anchor.Append('-');
            }
        }
        return anchor.ToString();
    }

    [GeneratedRegex(@"^### (GX\d{4}) (.+)$")]
    private static partial Regex Heading();
}
