using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.EmailIntake;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.Drafts;

// Shared setup for the draft pages: builders for drafts and a stubbed /api/drafts surface.
public abstract class DraftTestContext : VehicleTestContext
{
    protected static readonly Guid DefaultVehicleId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    protected static readonly Guid OtherVehicleId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    // Every body sent to the draft routes, in order, with the route it went to.
    protected List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];

    // The draft returned by GET /api/drafts/{id}; null answers 404.
    protected MaintenanceDraftDto? CurrentDraft { get; set; }

    // Returned by POST .../compatibility; defaults to an acceptable result.
    protected CompatibilityDto? CompatibilityAnswer { get; set; }

    // Returned by PUT .../kind; null answers with the current draft re-presented for the requested kind.
    protected Func<HttpRequestMessage, HttpResponseMessage>? KindAnswer { get; set; }

    protected Func<HttpRequestMessage, HttpResponseMessage>? AcceptAnswer { get; set; }

    protected Func<HttpRequestMessage, HttpResponseMessage>? DiscardAnswer { get; set; }

    protected HttpStatusCode? DraftFailure { get; set; }

    protected PagedResultDto<MaintenanceDraftSummaryDto>? DraftList { get; set; }

    // Returned by GET /api/emails; null answers 404.
    protected PagedResultDto<EmailHistoryItemDto>? HistoryList { get; set; }

    protected void RegisterDrafts()
    {
        RegisterApi(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.StartsWith("/api/drafts", StringComparison.Ordinal) && path != "/api/emails")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var body = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().Result;
            Calls.Add((request.Method, path + request.RequestUri.Query, body));

            if (DraftFailure is { } status)
            {
                return new HttpResponseMessage(status);
            }

            if (path == "/api/emails")
            {
                return HistoryList is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(HistoryList);
            }

            if (path == "/api/drafts" && request.Method == HttpMethod.Get)
            {
                return DraftList is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(DraftList);
            }

            if (CurrentDraft is null)
            {
                return Problem(HttpStatusCode.NotFound, "Not found");
            }

            if (path.EndsWith("/compatibility", StringComparison.Ordinal))
            {
                return Json(CompatibilityAnswer ?? Acceptable(JsonSerializer.Deserialize<DraftProposal>(body, JsonOptions)?.Kind));
            }

            if (path.EndsWith("/kind", StringComparison.Ordinal))
            {
                return KindAnswer?.Invoke(request) ?? Json(CurrentDraft);
            }

            if (path.EndsWith("/accept", StringComparison.Ordinal))
            {
                return AcceptAnswer?.Invoke(request)
                    ?? Json(new AcceptedDraftDto { Kind = DraftKind.ServiceRecord, VehicleId = DefaultVehicleId, RecordId = Guid.NewGuid() }, HttpStatusCode.Created);
            }

            if (path.EndsWith("/discard", StringComparison.Ordinal))
            {
                return DiscardAnswer?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return Json(CurrentDraft);
        });
    }

    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected JsonElement LastBodyTo(string pathSuffix, HttpMethod method) =>
        JsonDocument.Parse(Calls.Last(call => call.Method == method && call.Path.EndsWith(pathSuffix, StringComparison.Ordinal)).Body).RootElement;

    // A posted form that names the button pressed, as a browser sends it.
    protected static HttpContext Post(string action)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["action"] = action });
        return context;
    }

    protected static CompatibilityDto Acceptable(DraftKind? kind = DraftKind.ServiceRecord, params string[] warnings) =>
        new()
        {
            Kind = kind,
            IsAcceptable = true,
            Fields = [new CompatibilityFieldDto { Path = "serviceRecord.dateServiced", Status = CompatibilityStatus.Ok }],
            Warnings = warnings,
        };

    protected static CompatibilityDto NeedsAttention(DraftKind? kind = DraftKind.ServiceRecord, params (string Path, CompatibilityStatus Status, string? Message)[] fields) =>
        new()
        {
            Kind = kind,
            IsAcceptable = false,
            Fields = [.. fields.Select(field => new CompatibilityFieldDto { Path = field.Path, Status = field.Status, Message = field.Message })],
        };

    protected static ServiceRecordProposal SampleServiceRecord() =>
        new()
        {
            DateServiced = new DateOnly(2026, 10, 12),
            OdometerReading = 45_210,
            ShopOrPerson = "Joe's Garage",
            DocumentTotal = 200m,
            LineItems =
            [
                new ServiceLineItemRequest { Description = "Oil change", FlatCost = 40m },
                new ServiceLineItemRequest
                {
                    Description = "Brake job",
                    LaborHours = 1.5m,
                    LaborRate = 90m,
                    Parts = [new ServicePartRequest { Name = "Brake pad", Quantity = 2m, UnitPrice = 12.5m }],
                },
            ],
        };

    protected static MaintenanceDraftDto DraftDto(
        Guid? id = null,
        DraftState state = DraftState.NeedsReview,
        DraftKind? kind = DraftKind.ServiceRecord,
        DraftProposal? proposal = null,
        Guid? vehicleId = null,
        VehicleMatch match = VehicleMatch.Vin,
        IReadOnlyList<VehicleSuggestionDto>? candidates = null,
        bool hasAttachment = true,
        string contentType = "application/pdf",
        string? sender = "shop@example.com",
        string? subject = "Your invoice",
        CompatibilityDto? compatibility = null,
        string? notes = null,
        bool withoutVehicle = false) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            State = state,
            Kind = kind,
            Proposal = proposal ?? (kind == DraftKind.ServiceRecord ? new DraftProposal { Kind = kind, ServiceRecord = SampleServiceRecord() } : new DraftProposal { Kind = kind }),
            Vehicle = new DraftVehicleDto
            {
                VehicleId = withoutVehicle ? null : vehicleId ?? DefaultVehicleId,
                Match = withoutVehicle ? VehicleMatch.None : match,
                Candidates = candidates ??
                [
                    new VehicleSuggestionDto { VehicleId = DefaultVehicleId, Name = "2020 Ford F-150", Match = match },
                    new VehicleSuggestionDto { VehicleId = OtherVehicleId, Name = "2019 Subaru Outback", Match = VehicleMatch.None },
                ],
            },
            Source = hasAttachment
                ? new DraftSourceDto { Kind = "attachment", FileName = "invoice.pdf", ContentType = contentType, SizeBytes = 120_000 }
                : new DraftSourceDto { Kind = "text" },
            Email = state is DraftState.NeedsReview or DraftState.ManualEntry
                ? new DraftEmailDto { ArrivedAt = new DateTimeOffset(2026, 11, 3, 15, 30, 0, TimeSpan.Zero), Sender = sender, Subject = subject }
                : null,
            Compatibility = compatibility ?? Acceptable(kind),
            Notes = notes,
            HasAttachment = hasAttachment,
            ExpiresAt = new DateTimeOffset(2026, 12, 3, 15, 30, 0, TimeSpan.Zero),
        };

    protected static MaintenanceDraftSummaryDto DraftSummary(
        Guid? id = null,
        DraftState state = DraftState.NeedsReview,
        DraftKind? kind = DraftKind.ServiceRecord,
        string? vehicleName = "2020 Ford F-150",
        decimal? total = 200m) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            State = state,
            Kind = kind,
            VehicleName = vehicleName,
            ArrivedAt = new DateTimeOffset(2026, 11, 3, 15, 30, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 12, 3, 15, 30, 0, TimeSpan.Zero),
            HeadlineTotal = total,
        };

    protected static EmailHistoryItemDto HistoryItem(
        EmailHistoryOutcome outcome = EmailHistoryOutcome.AwaitingReview,
        DateTimeOffset? arrivedAt = null,
        params EmailHistoryRecordDto[] records) =>
        new()
        {
            SubmissionId = Guid.NewGuid(),
            ArrivedAt = arrivedAt ?? new DateTimeOffset(2026, 11, 3, 15, 30, 0, TimeSpan.Zero),
            Outcome = outcome,
            Records = records,
        };

    protected static PagedResultDto<EmailHistoryItemDto> HistoryPage(IReadOnlyList<EmailHistoryItemDto> items, int page = 1, int totalCount = -1, int pageSize = 10)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<EmailHistoryItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    protected static PagedResultDto<MaintenanceDraftSummaryDto> DraftsPage(IReadOnlyList<MaintenanceDraftSummaryDto> items, int page = 1, int totalCount = -1, int pageSize = 10)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<MaintenanceDraftSummaryDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }
}
