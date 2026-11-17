using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MyGarage.Web.Tests.Common;

// Guards the design tokens (docs/design-tokens.md, wwwroot/css/tokens.css): the contrast pairs the design promises,
// and that components take colours from tokens instead of hard-coding them.
public class DesignTokenTests
{
    private static readonly string WebProject = FindWebProject();

    private static string FindWebProject([CallerFilePath] string? sourceFile = null)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyGarage.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "MyGarage.Web");
    }

    private static string ReadCss(string relative) => File.ReadAllText(Path.Combine(WebProject, "wwwroot", relative));

    private static Dictionary<string, string> Block(string css, string selectorPattern)
    {
        var match = Regex.Match(css, selectorPattern + "\\s*\\{(?<body>[^}]*)\\}", RegexOptions.Singleline);
        Assert.True(match.Success, $"Token block not found: {selectorPattern}");

        return Regex.Matches(match.Groups["body"].Value, "(--mg-[a-z0-9-]+)\\s*:\\s*([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => Regex.Replace(m.Groups[2].Value, "/\\*.*?\\*/", string.Empty).Trim());
    }

    private static Dictionary<string, string> Palette(bool dark)
    {
        var css = ReadCss("css/tokens.css");
        var palette = Block(css, ":root,\\s*\\[data-bs-theme=\"light\"\\]");
        if (dark)
        {
            foreach (var (name, value) in Block(css, "\\[data-bs-theme=\"dark\"\\]"))
            {
                palette[name] = value;
            }
        }

        return palette;
    }

    private static double Luminance(string hex)
    {
        Assert.Matches("^#[0-9A-Fa-f]{6}$", hex);
        double Channel(int start)
        {
            var value = int.Parse(hex.AsSpan(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(1)) + (0.7152 * Channel(3)) + (0.0722 * Channel(5));
    }

    private static double Contrast(Dictionary<string, string> palette, string foreground, string background)
    {
        var a = Luminance(Resolve(palette, foreground));
        var b = Luminance(Resolve(palette, background));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static string Resolve(Dictionary<string, string> palette, string token)
    {
        var value = palette[token];
        while (value.StartsWith("var(", StringComparison.Ordinal))
        {
            value = palette[value[4..^1].Trim()];
        }

        return value;
    }

    public static TheoryData<string, string, double> Pairs() => new()
    {
        { "--mg-text", "--mg-bg", 4.5 },
        { "--mg-text", "--mg-surface", 4.5 },
        { "--mg-text", "--mg-surface-2", 4.5 },
        { "--mg-text-muted", "--mg-bg", 4.5 },
        { "--mg-text-muted", "--mg-surface", 4.5 },
        { "--mg-text-muted", "--mg-surface-2", 4.5 },
        { "--mg-accent-text", "--mg-bg", 4.5 },
        { "--mg-accent-text", "--mg-surface", 4.5 },
        { "--mg-accent-text", "--mg-surface-2", 4.5 },
        { "--mg-on-accent", "--mg-accent", 4.5 },
        { "--mg-ok", "--mg-surface", 4.5 },
        { "--mg-bad", "--mg-surface", 4.5 },
        { "--mg-on-bad", "--mg-bad", 4.5 },
        { "--mg-rail-text", "--mg-rail-bg", 4.5 },
        { "--mg-rail-active", "--mg-rail-bg", 4.5 },
        { "--mg-paper-ink", "--mg-paper", 4.5 },
        { "--mg-line-strong", "--mg-surface", 3.0 },
        { "--mg-line-strong", "--mg-surface-2", 3.0 },
        { "--mg-accent", "--mg-surface", 3.0 },
        { "--mg-accent", "--mg-bg", 3.0 },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void LightPaletteMeetsContrast(string foreground, string background, double minimum) =>
        Assert.True(Contrast(Palette(dark: false), foreground, background) >= minimum, $"{foreground} on {background} (light)");

    [Theory]
    [MemberData(nameof(Pairs))]
    public void DarkPaletteMeetsContrast(string foreground, string background, double minimum) =>
        Assert.True(Contrast(Palette(dark: true), foreground, background) >= minimum, $"{foreground} on {background} (dark)");

    [Fact]
    public void BothPalettesDefineTheSameSurfaceTextAndStatusTokens()
    {
        var light = Palette(dark: false);
        var dark = Palette(dark: true);

        foreach (var token in new[] { "--mg-bg", "--mg-surface", "--mg-surface-2", "--mg-line", "--mg-line-strong", "--mg-text", "--mg-text-muted", "--mg-accent", "--mg-accent-text", "--mg-ok", "--mg-bad", "--mg-rail-bg" })
        {
            Assert.True(light.ContainsKey(token), $"{token} missing from light");
            Assert.True(dark.ContainsKey(token), $"{token} missing from dark");
        }
    }

    [Fact]
    public void AppCssTakesColoursFromTokens_NotHexLiterals()
    {
        var violations = Regex.Matches(ReadCss("app.css"), "#[0-9A-Fa-f]{6}\\b").Select(m => m.Value).Distinct();

        Assert.Empty(violations);
    }

    // The pre-redesign names that were kept as aliases while pages were converted.
    private static readonly string[] LegacyNames =
    [
        "primary", "primary-hover", "primary-active", "primary-text", "secondary", "secondary-strong",
        "outline-bg", "outline-hover-bg", "outline-hover-border", "destructive-bg", "destructive-hover", "destructive-text",
        "danger-text", "night", "page-bg", "card-radius", "card-shadow", "mark-text",
        "notfound-code", "notfound-shadow", "notfound-label",
    ];

    [Fact]
    public void NoLegacyTokenNameRemainsInTheStylesheetsOrComponents()
    {
        var sources = Directory.GetFiles(WebProject, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".css", StringComparison.Ordinal) || path.EndsWith(".razor", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}"));

        var violations = sources
            .SelectMany(path => LegacyNames.Where(name => Regex.IsMatch(File.ReadAllText(path), $"--mg-{Regex.Escape(name)}(?![A-Za-z0-9-])")).Select(name => $"{Path.GetFileName(path)}: --mg-{name}"));

        Assert.Empty(violations);
    }

    [Fact]
    public void NoHeroOrHomePhotoStylesRemain() =>
        Assert.DoesNotMatch(new Regex("mg-hero|home-garage"), ReadCss("app.css"));
}