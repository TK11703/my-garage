using System.Net;
using System.Text.RegularExpressions;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Help;

public class FaqPageTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public FaqPageTests(WebAppFactory factory) => this.factory = factory;

    private async Task<(HttpStatusCode Status, string Html)> GetAsync(string? signedInAs = null)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        if (signedInAs is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, signedInAs);
        }

        var response = await client.GetAsync("/faq");
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task IsViewableWithoutSigningIn()
    {
        var (status, html) = await GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<title>Frequently Asked Questions – My Garage</title>", html);
        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Contains("Frequently Asked Questions</h1>", html);
        Assert.Contains("aria-label=\"Breadcrumb\"", html);
        Assert.DoesNotMatch("breadcrumb-item[^>]*>(<[^>]+>)*\\s*FAQ", html);
    }

    [Fact]
    public async Task AnonymousVisitorsSeeTheSignedOutBar()
    {
        var (_, html) = await GetAsync();

        Assert.Contains("href=\"/authentication/login\"", html);
        Assert.DoesNotContain("Account menu for", html);
    }

    [Fact]
    public async Task SignedInUsersSeeTheProfileMenu()
    {
        var (status, html) = await GetAsync("Ada Lovelace");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Account menu for Ada Lovelace", html);
    }

    [Fact]
    public async Task SignedInAccountMenuHasNoMyVehiclesItem()
    {
        var (_, html) = await GetAsync("Ada Lovelace");

        Assert.DoesNotMatch(@">\s*My vehicles\s*<", html);
        Assert.Contains("Preferences", html);
        Assert.Contains("Sign out", html);
    }

    [Fact]
    public async Task AddAVehicleAnswerDescribesTheNameFromYearMakeAndModel()
    {
        var (_, html) = await GetAsync();

        Assert.Contains("How do I add a vehicle?", html);
        Assert.Contains("Enter the year, make, and model; the other details are optional. The vehicle is named from its year, make, and model.", html);
        Assert.DoesNotContain("nickname", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ItIsLaidOutAsASearchFieldAndCardsOfQuestions()
    {
        var (_, html) = await GetAsync();
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        Assert.Contains("How can we help you?", document.QuerySelector("h2")!.TextContent);
        var search = document.QuerySelector("input[data-faq-search]")!;
        Assert.Equal("search", search.GetAttribute("type"));
        Assert.False(string.IsNullOrWhiteSpace(search.GetAttribute("aria-label")));

        var cards = document.QuerySelectorAll("[data-faq-category]");
        Assert.Equal(6, cards.Length);
        Assert.All(cards, card =>
        {
            Assert.NotNull(card.QuerySelector("h2"));
            Assert.NotNull(card.QuerySelector("svg.bi"));
            Assert.NotEmpty(card.QuerySelectorAll("[data-faq-item] details > summary"));
            Assert.NotNull(card.QuerySelector("button[data-faq-expand]"));
        });
        Assert.True(document.QuerySelectorAll("[data-faq-item]").Length >= 20);
    }

    [Fact]
    public async Task MaintenanceRecordsExplainsTheThreeWaysToAddARecord()
    {
        var (_, html) = await GetAsync();
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        var card = document.QuerySelectorAll("[data-faq-category]").Single(c => c.QuerySelector("h2")!.TextContent == "Maintenance records");
        var first = card.QuerySelector("[data-faq-item] details")!;
        Assert.Equal("How do I add maintenance records to my vehicle?", first.QuerySelector("summary")!.TextContent.Trim());
        var answer = first.QuerySelector("p")!.TextContent;
        Assert.Contains("Create a record yourself", answer);
        Assert.Contains("Upload a PDF or image", answer);
        Assert.Contains("body of the message or as an attachment", answer);
        Assert.Contains("mygarage_intake@outlook.com", answer);
    }

    [Fact]
    public async Task EveryQuestionHasAnAnswer_AndTheMaintenanceAnswersPointToTheAddMenu()
    {
        var (_, html) = await GetAsync();
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);

        Assert.All(document.QuerySelectorAll("[data-faq-item] details"), item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.QuerySelector("summary")!.TextContent));
            Assert.False(string.IsNullOrWhiteSpace(item.QuerySelector("p")!.TextContent));
        });
        Assert.DoesNotContain("+ button", html);
        Assert.Contains("choose Add in the bar at the top of the page", html);
    }
}
