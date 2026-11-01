using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MyGarage.Web.Tests.Common;

// Guards the UI standard (docs/ui-standard.md) against one-off styles in the Razor sources.
public class UiStandardConformanceTests
{
    private static readonly string[] Sources = FindSources();

    private static string[] FindSources([CallerFilePath] string? sourceFile = null)
    {
        // Start from this source file so the scan works wherever the build output goes.
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyGarage.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Directory.GetFiles(Path.Combine(directory.FullName, "src", "MyGarage.Web"), "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();
    }

    private static IEnumerable<string> Violations(Regex pattern, Func<string, bool>? skipFile = null)
    {
        foreach (var path in Sources)
        {
            if (skipFile?.Invoke(path) == true)
            {
                continue;
            }

            foreach (Match match in pattern.Matches(File.ReadAllText(path)))
            {
                yield return $"{Path.GetFileName(path)}: {match.Value.Trim()}";
            }
        }
    }

    [Fact]
    public void OnlyPageHeaderRendersAnH1()
    {
        var violations = Violations(new Regex("<h1[\\s>]"), path => Path.GetFileName(path) == "PageHeader.razor");

        Assert.Empty(violations);
    }

    [Fact]
    public void NoInlineStyles()
    {
        Assert.Empty(Violations(new Regex("\\sstyle=\"", RegexOptions.IgnoreCase)));
    }

    [Fact]
    public void NoSolidSecondaryButtons()
    {
        Assert.Empty(Violations(new Regex("(?<!outline-)btn-secondary")));
    }

    [Fact]
    public void NoFixedWidthContainer()
    {
        Assert.Empty(Violations(new Regex("class=\"(?:[^\"]*\\s)?container(?:\\s[^\"]*)?\"")));
    }

    [Fact]
    public void EveryAlertHasARole()
    {
        var violations = Violations(new Regex("<[a-z]+[^>]*\\balert alert-[^>]*>"))
            .Where(tag => !tag.Contains("role=", StringComparison.Ordinal));

        Assert.Empty(violations);
    }

    [Fact]
    public void FieldErrorsUseInvalidFeedback()
    {
        Assert.Empty(Violations(new Regex("text-danger small")));
    }
}
