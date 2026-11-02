using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MyGarage.Web.Tests.Common;

// Guards the UI standard (docs/ui-standard.md) against one-off styles in the Razor sources.
public class UiStandardConformanceTests
{
    private static readonly string[] Sources = FindSources();
    private static readonly string WebProject = FindWebProject();
    private static readonly string[] Scripts = ["lease-toggle.js", "pricing-toggle.js", "dropzone.js", "feature-suggest.js", "auto-submit.js", "clear-error.js"];

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

    [Fact]
    public void UserTextIsNeverRenderedAsMarkup()
    {
        Assert.Empty(Violations(new Regex("MarkupString")));
    }

    [Fact]
    public void TheServiceRecordSourcesAreScanned_AndUseNoTablesScriptsOrIconOnlyButtons()
    {
        var serviceRecordSources = Sources.Where(path => path.Contains($"{Path.DirectorySeparatorChar}ServiceRecords{Path.DirectorySeparatorChar}")).ToList();

        Assert.True(serviceRecordSources.Count >= 7);
        foreach (var path in serviceRecordSources)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch(new Regex("<table[\\s>]"), source);
            Assert.DoesNotMatch(new Regex("<script|jquery", RegexOptions.IgnoreCase), source);
            Assert.DoesNotMatch(new Regex("MarkupString"), source);
        }

        // Row-level controls are small and named after what they act on.
        var row = File.ReadAllText(serviceRecordSources.Single(path => Path.GetFileName(path) == "ServiceLineItemRow.razor"));
        Assert.Contains("aria-label=\"@($\"Edit {Line.Description}\")\"", row);
        Assert.Contains("aria-label=\"@($\"Delete {Line.Description}\")\"", row);
    }

    [Fact]
    public void LookupListsAreNotTables()
    {
        var violations = Violations(new Regex("<table[\\s>]"), path => !path.Contains($"{Path.DirectorySeparatorChar}Preferences{Path.DirectorySeparatorChar}"));

        Assert.Empty(violations);
    }

    [Fact]
    public void NoJQueryOrCustomScriptsInThePreferencesAndTabSources()
    {
        var violations = Violations(
            new Regex("<script|jquery", RegexOptions.IgnoreCase),
            path => !path.Contains($"{Path.DirectorySeparatorChar}Preferences{Path.DirectorySeparatorChar}") && Path.GetFileName(path) != "TabNav.razor");

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("lease-toggle.js")]
    [InlineData("pricing-toggle.js")]
    [InlineData("dropzone.js")]
    [InlineData("feature-suggest.js")]
    [InlineData("auto-submit.js")]
    [InlineData("clear-error.js")]
    public void EachScript_ExistsAndIsLoadedAsAModuleFromTheAppShell(string script)
    {
        Assert.True(File.Exists(Path.Combine(WebProject, "wwwroot", "js", script)));

        var shell = File.ReadAllText(Path.Combine(WebProject, "Components", "App.razor"));
        Assert.Matches(new Regex($"<script type=\"module\" src=\"@Assets\\[\"js/{Regex.Escape(script)}\"\\]\"></script>"), shell);
    }

    [Fact]
    public void TheJsFolderHoldsOnlyTheKnownScripts()
    {
        var files = Directory.GetFiles(Path.Combine(WebProject, "wwwroot", "js"), "*.js").Select(Path.GetFileName);

        Assert.Equal(Scripts.Order(), files.Order());
    }

    [Theory]
    [InlineData("lease-toggle.js")]
    [InlineData("pricing-toggle.js")]
    [InlineData("dropzone.js")]
    [InlineData("feature-suggest.js")]
    [InlineData("auto-submit.js")]
    [InlineData("clear-error.js")]
    public void Scripts_UseNoJQueryEvalOrInnerHtml(string script)
    {
        var source = File.ReadAllText(Path.Combine(WebProject, "wwwroot", "js", script));

        Assert.DoesNotMatch(new Regex("jquery", RegexOptions.IgnoreCase), source);
        Assert.DoesNotMatch(new Regex(@"\$\("), source);
        Assert.DoesNotMatch(new Regex(@"\beval\("), source);
        Assert.DoesNotMatch(new Regex(@"innerHTML|outerHTML|insertAdjacentHTML|document\.write"), source);
    }

    [Fact]
    public void TheAppShell_HasNoInlineScriptsOrJQuery()
    {
        var shell = File.ReadAllText(Path.Combine(WebProject, "Components", "App.razor"));

        Assert.DoesNotMatch(new Regex("jquery", RegexOptions.IgnoreCase), shell);
        Assert.DoesNotMatch(new Regex(@"<script(?![^>]*\bsrc=)"), shell);
    }
}
