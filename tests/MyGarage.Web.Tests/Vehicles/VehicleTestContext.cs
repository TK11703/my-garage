using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Forms.Mapping;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Vehicles;
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

    // Answered to GET /api/vehicles/filter-options.
    protected VehicleFilterOptionsDto FilterOptions { get; set; } = new() { Makes = [], Colors = [], Tags = [] };

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
                return Json(FilterOptions);
            }

            if (Notes is not null && request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith("/notes", StringComparison.Ordinal))
            {
                return Json(Notes);
            }

            return respond(request);
        });
        Services.AddSingleton(new VehiclesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
    }

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

    protected static VehicleSummaryDto Summary(string nickname, string make = "Ford", string model = "F-150", int? mileage = null, Guid? mainImageId = null) =>
        new() { Id = Guid.NewGuid(), Nickname = nickname, Make = make, Model = model, Mileage = mileage, MainImageId = mainImageId };

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

    protected static VehicleDetailDto Detail(Guid? id = null, string nickname = "Daily") =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Nickname = nickname,
            Make = "Ford",
            Model = "F-150",
            Trim = "XLT",
            Color = "Blue",
            Mileage = 42_000,
            Doors = 4,
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
