using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Forms.Mapping;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Users;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Common;
using MyGarage.Web.Drafts;
using MyGarage.Web.FuelEnergyEntries;
using MyGarage.Web.Inspections;
using MyGarage.Web.Insurance;
using MyGarage.Web.Registrations;
using MyGarage.Web.Lookups;
using MyGarage.Web.Notifications;
using MyGarage.Web.ServiceRecords;
using MyGarage.Web.Taxes;
using MyGarage.Web.VehicleDocuments;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

// Shared setup for vehicle component tests: a stubbed API handler and antiforgery state for static-SSR forms.
public abstract class VehicleTestContext : BunitContext
{
    protected List<HttpRequestMessage> Requests { get; } = [];

    // Requests for the garage list itself (not the filter options).
    protected IEnumerable<HttpRequestMessage> ListRequests => Requests.Where(request => request.RequestUri!.AbsolutePath == "/api/vehicles");

    // Answered to GET /api/vehicles/{id}/notes; null sends the request to the test's own handler.
    protected PagedResultDto<JournalNoteDto>? Notes { get; set; } = NotesPage([]);

    // Answered to GET /api/vehicles/{id}/service-records; null sends the request to the test's own handler.
    protected PagedResultDto<ServiceRecordSummaryDto>? ServiceRecords { get; set; } = ServiceRecordsPage([]);

    // When set, GET /api/vehicles/{id}/service-records answers with this status instead of the page.
    protected HttpStatusCode? ServiceRecordsFailure { get; set; }

    // Answered to GET /api/vehicles/{id}/<segment> for the new Maintenance lists (fuel-energy-entries, inspections,
    // registrations, insurance, taxes, documents); a segment with no entry answers with an empty page.
    protected Dictionary<string, object> MaintenanceLists { get; } = [];

    // When a segment is present, its list answers with this status instead of the page.
    protected Dictionary<string, HttpStatusCode> MaintenanceListFailures { get; } = [];

    // Takes precedence over MaintenanceLists for a segment, for tests that answer by page.
    protected Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> MaintenanceListResponders { get; } = [];

    private static readonly string[] MaintenanceSegments = ["fuel-energy-entries", "inspections", "registrations", "insurance", "taxes", "documents"];

    // Answered to GET /api/vehicles/filter-options.
    protected VehicleFilterOptionsDto FilterOptions { get; set; } = new() { Makes = [], Colors = [], Tags = [] };

    // When set, GET /api/vehicles/filter-options answers with this status instead of the options.
    protected HttpStatusCode? FilterOptionsFailure { get; set; }

    // Answered to GET /api/lookups (hidden items are left out unless includeHidden=true).
    protected LookupsDto Lookups { get; set; } = DefaultLookups();

    // When set, GET /api/lookups answers with this status instead of the lookups.
    protected HttpStatusCode? LookupsFailure { get; set; }

    // Answered to GET /api/users/me.
    protected CurrentUserDto CurrentUser { get; set; } = new() { UserId = "user-1", DisplayName = "Ada Lovelace", Email = "ada@example.com" };

    // When set, GET /api/users/me answers with this status instead of the current user.
    protected HttpStatusCode? CurrentUserFailure { get; set; }

    protected static LookupItemDto Item(string name, bool isDefault = true, bool isHidden = false) =>
        new() { Id = Guid.NewGuid(), Name = name, IsDefault = isDefault, IsHidden = isHidden };

    protected static LookupsDto DefaultLookups() =>
        new()
        {
            VehicleTypes = [Item("Sedan"), Item("SUV"), Item("Truck")],
            FuelTypes = [Item("Gasoline"), Item("Diesel"), Item("Electric"), Item("Hybrid")],
            Transmissions = [Item("Automatic"), Item("Manual")],
            Drivetrains = [Item("FWD"), Item("RWD"), Item("AWD"), Item("4WD")],
        };

    protected VehicleTestContext()
    {
        Services.AddSingleton<AntiforgeryStateProvider, NoAntiforgeryStateProvider>();
        Services.AddSingleton<IFormValueMapper, NoFormValueMapper>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    protected void NavigateTo(string relativeUri) =>
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(relativeUri);

    protected void RegisterApi(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(request =>
        {
            Requests.Add(request);
            if (request.RequestUri!.AbsolutePath == "/api/vehicles/filter-options")
            {
                return FilterOptionsFailure is { } optionsFailure ? new HttpResponseMessage(optionsFailure) : Json(FilterOptions);
            }

            if (request.RequestUri.AbsolutePath == "/api/users/me")
            {
                return CurrentUserFailure is { } userFailure ? new HttpResponseMessage(userFailure) : Json(CurrentUser);
            }

            if (request.RequestUri.AbsolutePath == "/api/lookups" && request.Method == HttpMethod.Get)
            {
                if (LookupsFailure is { } failureStatus)
                {
                    return new HttpResponseMessage(failureStatus);
                }

                return Json(request.RequestUri.Query.Contains("includeHidden=true", StringComparison.Ordinal) ? Lookups : WithoutHidden(Lookups));
            }

            if (Notes is not null && request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith("/notes", StringComparison.Ordinal))
            {
                return Json(Notes);
            }

            if (ServiceRecords is not null && request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith("/service-records", StringComparison.Ordinal))
            {
                return ServiceRecordsFailure is { } recordsFailure ? new HttpResponseMessage(recordsFailure) : Json(ServiceRecords);
            }

            if (request.Method == HttpMethod.Get && MaintenanceSegments.Contains(request.RequestUri.AbsolutePath.Split('/')[^1]))
            {
                var segment = request.RequestUri.AbsolutePath.Split('/')[^1];
                if (MaintenanceListResponders.TryGetValue(segment, out var responder))
                {
                    return responder(request);
                }

                if (MaintenanceListFailures.TryGetValue(segment, out var listFailure))
                {
                    return new HttpResponseMessage(listFailure);
                }

                return MaintenanceLists.TryGetValue(segment, out var page)
                    ? Json(page)
                    : Json(new { items = Array.Empty<object>(), page = 1, pageSize = 10, totalCount = 0, totalPages = 0 });
            }

            return respond(request);
        });
        Services.AddSingleton(new VehiclesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new LookupsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new GarageApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new ServiceRecordsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new FuelEnergyEntriesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new InspectionsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new RegistrationsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new InsuranceApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new TaxesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new DraftsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new NotificationsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
        Services.AddSingleton(new VehicleDocumentsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
    }

    private static LookupsDto WithoutHidden(LookupsDto lookups) =>
        new()
        {
            VehicleTypes = [.. lookups.VehicleTypes.Where(item => !item.IsHidden)],
            FuelTypes = [.. lookups.FuelTypes.Where(item => !item.IsHidden)],
            Transmissions = [.. lookups.Transmissions.Where(item => !item.IsHidden)],
            Drivetrains = [.. lookups.Drivetrains.Where(item => !item.IsHidden)],
        };

    protected static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };

    protected static HttpResponseMessage Problem(HttpStatusCode status, string title) =>
        new(status)
        {
            Content = new StringContent($$"""{"title":"{{title}}","status":{{(int)status}}}""", System.Text.Encoding.UTF8, "application/problem+json"),
        };

    protected static HttpResponseMessage ValidationProblem(params (string Field, string Message)[] errors) =>
        new(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                title = "One or more validation errors occurred.",
                status = 400,
                errors = errors.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()),
            }),
        };

    protected static VehicleSummaryDto Summary(
        string make = "Ford",
        string model = "F-150",
        int? mileage = null,
        Guid? mainImageId = null,
        int? year = 2020,
        string? fuelType = null,
        string? transmission = null,
        OwnershipType? ownership = null,
        int? purchasedYear = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Make = make,
            Model = model,
            Mileage = mileage,
            MainImageId = mainImageId,
            Year = year,
            FuelType = fuelType,
            Transmission = transmission,
            Ownership = ownership,
            PurchasedYear = purchasedYear,
        };

    protected static PagedResultDto<VehicleSummaryDto> Page(IReadOnlyList<VehicleSummaryDto> items, int page = 1, int totalCount = -1, int pageSize = 12)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<VehicleSummaryDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    protected static JournalNoteDto Note(string body, string? category = null, DateTimeOffset? createdAt = null, DateTimeOffset? updatedAt = null)
    {
        var created = createdAt ?? new DateTimeOffset(2026, 10, 31, 15, 30, 0, TimeSpan.Zero);
        return new JournalNoteDto { Id = Guid.NewGuid(), Body = body, Category = category, CreatedAt = created, UpdatedAt = updatedAt ?? created };
    }

    protected static PagedResultDto<JournalNoteDto> NotesPage(IReadOnlyList<JournalNoteDto> items, int page = 1, int totalCount = -1, int pageSize = 10)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<JournalNoteDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    protected static ServiceRecordSummaryDto RecordSummary(
        DateOnly? date = null, int odometer = 45_000, string? shop = "Joe's Garage", int lineItems = 0, decimal total = 0m, bool fromEmail = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            DateServiced = date ?? new DateOnly(2026, 5, 1),
            OdometerReading = odometer,
            ShopOrPerson = shop,
            LineItemCount = lineItems,
            TotalCost = total,
            FromEmail = fromEmail,
        };

    protected static PagedResultDto<ServiceRecordSummaryDto> ServiceRecordsPage(
        IReadOnlyList<ServiceRecordSummaryDto> items, int page = 1, int totalCount = -1, int pageSize = 10)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<ServiceRecordSummaryDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }

    protected static ServiceRecordDto RecordDetail(
        Guid? vehicleId = null,
        Guid? id = null,
        DateOnly? date = null,
        int odometer = 45_000,
        string? shop = "Joe's Garage",
        IReadOnlyList<ServiceLineItemDto>? lineItems = null,
        decimal? total = null,
        decimal? tax = null)
    {
        var items = lineItems ?? [];
        return new ServiceRecordDto
        {
            Id = id ?? Guid.NewGuid(),
            VehicleId = vehicleId ?? Guid.NewGuid(),
            DateServiced = date ?? new DateOnly(2026, 5, 1),
            OdometerReading = odometer,
            ShopOrPerson = shop,
            LineItems = items,
            Tax = tax,
            TotalCost = total ?? items.Sum(item => item.TotalCost) + (tax ?? 0m),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    protected static VehicleDetailDto Detail(Guid? id = null, string make = "Ford", string model = "F-150", int? year = 2020) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Make = make,
            Model = model,
            Trim = "XLT",
            Year = year,
            Color = "Blue",
            Mileage = 42_000,
            Doors = 4,
            Tags = [],
            Images = [],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    // A vehicle with every new field filled in.
    protected static VehicleDetailDto FullDetail(Guid? id = null, string make = "Subaru", string model = "Outback", int? year = 2019) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Vin = "1HGCM82633A004352",
            Make = make,
            Model = model,
            Trim = "Limited",
            Year = year,
            VehicleType = new LookupValueDto { Id = Guid.NewGuid(), Name = "Wagon" },
            Ownership = OwnershipType.Leased,
            PurchasedOn = new DateOnly(2024, 3, 15),
            LeaseEndsOn = new DateOnly(2027, 3, 14),
            Engine = "2.5L flat-4",
            Horsepower = 182,
            FuelType = new LookupValueDto { Id = Guid.NewGuid(), Name = "Gasoline" },
            Transmission = new LookupValueDto { Id = Guid.NewGuid(), Name = "Automatic" },
            Mileage = 42_000,
            Color = "Blue",
            Seats = 5,
            Doors = 4,
            Drivetrain = new LookupValueDto { Id = Guid.NewGuid(), Name = "AWD" },
            WheelSize = 17.5m,
            TireSize = "225/60R17",
            Tags = [],
            Images = [],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class NoFormValueMapper : IFormValueMapper
    {
        public bool CanMap(Type valueType, string mappingScopeName, string? formName) => false;

        public void Map(FormValueMappingContext context)
        {
        }
    }

    private sealed class NoAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
    }
}
