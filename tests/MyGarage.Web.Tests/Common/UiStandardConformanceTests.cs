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

    private static readonly string[] MaintenanceFolders = ["FuelEnergyEntries", "Inspections", "Registrations", "Insurance", "Taxes", "Maintenance"];

    private static List<string> MaintenanceSources() =>
        [.. Sources.Where(path => MaintenanceFolders.Any(folder => path.Contains($"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}")))];

    [Fact]
    public void TheMaintenanceSourcesAreScanned_AndUseNoTablesScriptsMarkupOrInlineStyles()
    {
        var sources = MaintenanceSources();

        // Five features with a form, create, edit, and list each, plus the six shared Maintenance components.
        Assert.True(sources.Count >= 26, $"Only {sources.Count} Maintenance sources were found.");
        foreach (var path in sources)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch(new Regex("<table[\\s>]"), source);
            Assert.DoesNotMatch(new Regex("<script|jquery", RegexOptions.IgnoreCase), source);
            Assert.DoesNotMatch(new Regex("MarkupString"), source);
            Assert.DoesNotMatch(new Regex("\\sstyle=\"", RegexOptions.IgnoreCase), source);
        }
    }

    [Fact]
    public void EveryMaintenanceFormFieldHasALabel()
    {
        var forms = MaintenanceSources().Where(path => path.EndsWith("Form.razor", StringComparison.Ordinal)).ToList();

        Assert.Equal(5, forms.Count);
        foreach (var path in forms)
        {
            var source = File.ReadAllText(path);
            var fields = Regex.Matches(source, "<Input(?:Text|Number|Date|Select)\\b[^>]*?\\bid=\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value)
                .ToList();

            Assert.NotEmpty(fields);
            foreach (var id in fields)
            {
                Assert.True(source.Contains($"<label for=\"{id}\"", StringComparison.Ordinal), $"{Path.GetFileName(path)}: no label for {id}");
            }
        }
    }

    [Fact]
    public void EveryMaintenanceEditLinkIsNamedAfterWhatItEdits()
    {
        var lists = MaintenanceSources().Where(path => path.EndsWith("List.razor", StringComparison.Ordinal)).ToList();

        Assert.Equal(5, lists.Count);
        foreach (var path in lists)
        {
            var source = File.ReadAllText(path);
            Assert.Contains("data-testid=\"edit-entry\"", source);
            Assert.Contains("aria-label=\"@", source);
            Assert.Contains("btn btn-sm", source);
        }
    }

    private static readonly string[] EmailIntakeFolders = ["Drafts", "Notifications", "VehicleDocuments"];

    private static List<string> EmailIntakeSources() =>
        [.. Sources.Where(path => EmailIntakeFolders.Any(folder => path.Contains($"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}")))];

    [Fact]
    public void TheEmailIntakeSourcesAreScanned_AndUseNoTablesScriptsMarkupOrInlineStyles()
    {
        var sources = EmailIntakeSources();

        // The list and review pages, the service record editor, the other kinds' fields, the compatibility panel,
        // the notifications menu, and the documents section.
        Assert.True(sources.Count >= 7, $"Only {sources.Count} email intake sources were found.");
        foreach (var path in sources)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotMatch(new Regex("<table[\\s>]"), source);
            Assert.DoesNotMatch(new Regex("<script|jquery", RegexOptions.IgnoreCase), source);
            Assert.DoesNotMatch(new Regex("MarkupString"), source);
            Assert.DoesNotMatch(new Regex("\\sstyle=\"", RegexOptions.IgnoreCase), source);
            Assert.DoesNotMatch(new Regex("<h1[\\s>]"), source);
        }
    }

    [Fact]
    public void EveryEmailIntakeFormControlHasALabel()
    {
        var controls = 0;
        foreach (var path in EmailIntakeSources())
        {
            var source = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(source, "<(?:input|select)\\b[^>]*?\\bid=\"([^\"]+)\"[^>]*>"))
            {
                if (match.Value.Contains("type=\"hidden\"", StringComparison.Ordinal))
                {
                    continue;
                }

                controls++;
                var id = match.Groups[1].Value;
                Assert.True(
                    Regex.IsMatch(source, $"<label\\b[^>]*\\bfor=\"{Regex.Escape(id)}\""),
                    $"{Path.GetFileName(path)}: no label for {id}");
            }
        }

        Assert.True(controls >= 20, $"Only {controls} form controls were found.");
    }

    [Fact]
    public void EveryEmailIntakeIconOnlyControlHasAnAccessibleName()
    {
        // A button or link with an icon and no text must say what it does.
        foreach (var path in EmailIntakeSources())
        {
            var source = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(source, "<(button|a)\\b([^>]*)>\\s*<Icon\\b[^>]*/>\\s*</\\1>"))
            {
                Assert.Contains("aria-label=", match.Groups[2].Value);
            }
        }
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
