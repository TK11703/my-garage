using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MyGarage.Web.Tests.Common;

// Fonts are self-hosted: every face the stylesheet names is shipped with its licence, within the size budget,
// and the document never reaches for an external font host.
public class FontDeliveryTests
{
    private const long BudgetBytes = 250 * 1024;

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

    private static string FontsCss => File.ReadAllText(Path.Combine(WebProject, "wwwroot", "css", "fonts.css"));

    [Fact]
    public void EveryFontFileTheStylesheetNamesExists_AndLoadsWithFontDisplaySwap()
    {
        var urls = Regex.Matches(FontsCss, "url\\(\"\\.\\./fonts/([^\"]+\\.woff2)\"\\)").Select(m => m.Groups[1].Value).ToList();

        Assert.True(urls.Count >= 5, "Expected at least the display, body, and mono faces.");
        Assert.All(urls, name => Assert.True(File.Exists(Path.Combine(WebProject, "wwwroot", "fonts", name)), $"{name} is missing"));
        Assert.Equal(Regex.Matches(FontsCss, "@font-face").Count - Regex.Matches(FontsCss, "src: local\\(").Count, urls.Count);
        Assert.All(Regex.Matches(FontsCss, "@font-face\\s*\\{[^}]*\\}").Where(m => m.Value.Contains("url(")), m => Assert.Contains("font-display: swap", m.Value));
    }

    [Fact]
    public void EachFamilyShipsItsLicence()
    {
        var fonts = Path.Combine(WebProject, "wwwroot", "fonts");

        foreach (var family in new[] { "ChakraPetch", "Inter", "JetBrainsMono" })
        {
            Assert.True(File.Exists(Path.Combine(fonts, $"OFL-{family}.txt")), $"OFL-{family}.txt is missing");
        }
    }

    [Fact]
    public void TheFontFilesStayWithinTheSizeBudget()
    {
        var total = Directory.GetFiles(Path.Combine(WebProject, "wwwroot", "fonts"), "*.woff2").Sum(path => new FileInfo(path).Length);

        Assert.True(total <= BudgetBytes, $"Fonts total {total} bytes; the budget is {BudgetBytes}.");
    }

    [Fact]
    public void NoStylesheetOrDocumentReachesAnExternalFontHost()
    {
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(WebProject, "Components", "App.razor")),
            FontsCss,
            File.ReadAllText(Path.Combine(WebProject, "wwwroot", "css", "tokens.css")),
            File.ReadAllText(Path.Combine(WebProject, "wwwroot", "app.css")),
        };

        Assert.All(sources, source => Assert.DoesNotMatch("fonts\\.(googleapis|gstatic)\\.com|use\\.typekit|fontawesome|cdn\\.", source));
    }

    [Fact]
    public void TheDocumentPreloadsTheDisplayAndBodyFacesAndLoadsTheStylesInOrder()
    {
        var app = File.ReadAllText(Path.Combine(WebProject, "Components", "App.razor"));

        Assert.Contains("rel=\"preload\" href=\"fonts/ChakraPetch-700.woff2\"", app);
        Assert.Contains("rel=\"preload\" href=\"fonts/Inter-Variable.woff2\"", app);
        Assert.Equal(2, Regex.Matches(app, "as=\"font\" type=\"font/woff2\" crossorigin").Count);

        var order = new[] { "bootstrap.min.css", "css/fonts.css", "css/tokens.css", "app.css" }
            .Select(name => app.IndexOf(name, StringComparison.Ordinal)).ToArray();
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.OrderBy(i => i), order);
    }
}
