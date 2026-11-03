using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MyGarage.Api.Drafts;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.ServiceRecords;

namespace MyGarage.Api.Tests.EmailIntake;

// Every route in specs/007-email-ingestion-api/contracts/api.openapi.yaml is checked against the four ways a caller can be wrong:
// no token, the workflow's app-only token on a user route, a user token on the intake route, and another user's token on an id.
public partial class AuthorizationMatrixTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "matrix-alice-oid";
    private const string Bob = "matrix-bob-oid";
    private const string IntakeRoute = "POST /api/intake/emails";

    private static readonly DateOnly Day = new(2025, 10, 20);

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [GeneratedRegex(@"^  (/api/\S+):\s*$")]
    private static partial Regex PathLine();

    [GeneratedRegex(@"^    (get|post|put|delete|patch):\s*$")]
    private static partial Regex MethodLine();

    // "POST /api/drafts/{draftId}/accept" for every operation in the contract.
    private static List<string> ContractRoutes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "specs", "007-email-ingestion-api", "contracts", "api.openapi.yaml")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var routes = new List<string>();
        string? path = null;
        foreach (var line in File.ReadLines(Path.Combine(directory.FullName, "specs", "007-email-ingestion-api", "contracts", "api.openapi.yaml")))
        {
            if (line.StartsWith("components:", StringComparison.Ordinal))
            {
                break;
            }

            if (PathLine().Match(line) is { Success: true } pathMatch)
            {
                path = pathMatch.Groups[1].Value;
            }
            else if (path is not null && MethodLine().Match(line) is { Success: true } methodMatch)
            {
                routes.Add($"{methodMatch.Groups[1].Value.ToUpperInvariant()} {path}");
            }
        }

        return routes;
    }

    private sealed record Seeded(Guid DraftId, Guid NotificationId, Guid VehicleId, Guid DocumentId, Guid BobVehicleId);

    private static DraftProposal ServiceProposal() =>
        new()
        {
            Kind = DraftKind.ServiceRecord,
            ServiceRecord = new ServiceRecordProposal
            {
                DateServiced = Day,
                OdometerReading = 45_500,
                ShopOrPerson = "Joe's Garage",
                LineItems = [new ServiceLineItemRequest { Description = "Oil change", FlatCost = 59.99m }],
            },
        };

    // Alice's vehicle, kept document, draft (with a stored PDF), and notification, plus a vehicle for Bob.
    private async Task<Seeded> SeedAsync()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice);
        var document = await factory.InsertVehicleDocumentAsync(vehicle);
        var submission = await factory.InsertSubmissionAsync(Alice, status: "ready");
        var attachment = await factory.InsertAttachmentAsync(submission, Alice, content: IntakeClient.PdfBytes);
        var draft = await factory.InsertDraftAsync(
            Alice, submission, "needs_review", "service_record", vehicle, attachment, "none", DraftProposalJson.Serialize(ServiceProposal()));
        var notification = await factory.InsertNotificationAsync(Alice, draft);
        var bobVehicle = await factory.InsertVehicleAsync(Bob);
        return new Seeded(draft, notification, vehicle, document, bobVehicle);
    }

    private static string Url(string route, Seeded seeded) =>
        route[(route.IndexOf(' ') + 1)..]
            .Replace("{draftId}", seeded.DraftId.ToString())
            .Replace("{notificationId}", seeded.NotificationId.ToString())
            .Replace("{vehicleId}", seeded.VehicleId.ToString())
            .Replace("{documentId}", seeded.DocumentId.ToString());

    private static HttpRequestMessage Request(string route, string url, object? body = null)
    {
        var method = new HttpMethod(route[..route.IndexOf(' ')]);
        return new HttpRequestMessage(method, url) { Content = method == HttpMethod.Get || method == HttpMethod.Delete ? null : JsonContent.Create(body ?? new { }) };
    }

    // Bodies that pass validation, so a 404 can only come from the ownership check and not from a 400 that runs first.
    private static object? ValidBody(string route, Seeded seeded) =>
        route switch
        {
            "POST /api/drafts/{draftId}/compatibility" => ServiceProposal(),
            "PUT /api/drafts/{draftId}/kind" => new ChangeDraftKindRequest { Kind = DraftKind.Tax },
            "POST /api/drafts/{draftId}/accept" => new AcceptDraftRequest { VehicleId = seeded.BobVehicleId, Proposal = ServiceProposal() },
            "POST /api/intake/emails" => IntakeClient.NewEmail("someone@nowhere.test"),
            _ => null,
        };

    private static async Task<string?> StatusProblem(HttpClient client, string route, Seeded seeded, params HttpStatusCode[] allowed)
    {
        var response = await client.SendAsync(Request(route, Url(route, seeded), ValidBody(route, seeded)));
        return allowed.Contains(response.StatusCode) ? null : $"{route} answered {(int)response.StatusCode}";
    }

    [Fact]
    public void TheMatrixCoversEveryRouteInTheContract()
    {
        var routes = ContractRoutes();

        Assert.Equal(16, routes.Count);
        Assert.Contains(IntakeRoute, routes);
        Assert.Equal(routes.Count, routes.Distinct().Count());
    }

    [Fact]
    public async Task EveryRoute_IsRefusedWithoutAToken()
    {
        var seeded = await SeedAsync();
        var client = factory.CreateClientWithToken(null);
        var failures = new List<string>();

        foreach (var route in ContractRoutes())
        {
            if (await StatusProblem(client, route, seeded, HttpStatusCode.Unauthorized) is { } failure)
            {
                failures.Add(failure);
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task EveryUserRoute_RefusesTheWorkflowsAppOnlyToken()
    {
        var seeded = await SeedAsync();
        var client = factory.CreateClientForWorkflow();
        var failures = new List<string>();

        foreach (var route in ContractRoutes().Where(route => route != IntakeRoute))
        {
            // The framework may reject an app-only token before the policy runs (401); either way nothing is served.
            if (await StatusProblem(client, route, seeded, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized) is { } failure)
            {
                failures.Add(failure);
            }
        }

        Assert.Empty(failures);
        Assert.Equal("needs_review", (string)(await factory.ExecuteScalarAsync("SELECT state FROM maintenance_drafts WHERE id = @id", ("id", seeded.DraftId)))!);
        Assert.Equal(1L, await factory.CountRowsAsync("vehicle_documents"));
        Assert.Equal(1L, await factory.CountRowsAsync("notifications"));
    }

    [Fact]
    public async Task TheIntakeRoute_RefusesAUserToken()
    {
        var seeded = await SeedAsync();
        var before = await factory.CountRowsAsync("email_submissions");

        Assert.Null(await StatusProblem(factory.CreateClientForUser(Alice, "alice@contoso.test"), IntakeRoute, seeded, HttpStatusCode.Forbidden));
        Assert.Equal(before, await factory.CountRowsAsync("email_submissions"));
    }

    [Fact]
    public async Task EveryIdAddressedRoute_Answers404ToAnotherUser_AndChangesNothing()
    {
        var seeded = await SeedAsync();
        var bob = factory.CreateClientForUser(Bob, "bob@contoso.test");
        var failures = new List<string>();

        foreach (var route in ContractRoutes().Where(route => route.Contains('{')))
        {
            if (await StatusProblem(bob, route, seeded, HttpStatusCode.NotFound) is { } failure)
            {
                failures.Add(failure);
            }
        }

        Assert.Empty(failures);
        Assert.Equal("needs_review", (string)(await factory.ExecuteScalarAsync("SELECT state FROM maintenance_drafts WHERE id = @id", ("id", seeded.DraftId)))!);
        Assert.Equal(1L, await factory.CountRowsAsync("vehicle_documents"));
        Assert.Equal(0L, await factory.CountRowsAsync("service_records"));
        Assert.Null(await factory.ExecuteScalarAsync("SELECT read_at FROM notifications WHERE id = @id", ("id", seeded.NotificationId)));
        Assert.True(await factory.DocumentBlobExistsAsync(seeded.VehicleId, seeded.DocumentId));
    }
}
