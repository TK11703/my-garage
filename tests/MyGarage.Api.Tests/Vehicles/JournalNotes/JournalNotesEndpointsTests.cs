using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.JournalNotes;

public class JournalNotesEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<VehicleDetailDto> CreateVehicleAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Nickname = "Daily", Make = "Ford", Model = "F-150" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    private static Task<HttpResponseMessage> AddNoteAsync(HttpClient client, Guid vehicleId, string? body, string? category = null) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/notes", new JournalNoteRequest { Body = body, Category = category });

    private static async Task<JournalNoteDto> AddAsync(HttpClient client, Guid vehicleId, string body, string? category = null)
    {
        var response = await AddNoteAsync(client, vehicleId, body, category);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JournalNoteDto>())!;
    }

    private static async Task<PagedResultDto<JournalNoteDto>> ListAsync(HttpClient client, Guid vehicleId, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<JournalNoteDto>>($"/api/vehicles/{vehicleId}/notes{query}"))!;

    [Fact]
    public async Task Post_WithFreeTextCategory_Returns201AndTheNoteIsListedFirst()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await AddAsync(client, vehicle.Id, "older note");

        var response = await AddNoteAsync(client, vehicle.Id, "  Changed the oil  ", "  Oil change (5W-30) ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var note = await response.Content.ReadFromJsonAsync<JournalNoteDto>();
        Assert.NotNull(note);
        Assert.Equal("Changed the oil", note.Body);
        Assert.Equal("Oil change (5W-30)", note.Category);
        var page = await ListAsync(client, vehicle.Id);
        Assert.Equal(["Changed the oil", "older note"], page.Items.Select(n => n.Body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_BlankCategory_IsStoredAsNone(string? category)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var note = await AddAsync(client, vehicle.Id, "no category", category);

        Assert.Null(note.Category);
    }

    [Fact]
    public async Task Post_ReturnsMarkupCharactersUnchanged()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var note = await AddAsync(client, vehicle.Id, "<script>alert(1)</script> \"quoted\"", "<b>cat</b>");

        Assert.Equal("<script>alert(1)</script> \"quoted\"", note.Body);
        Assert.Equal("<b>cat</b>", note.Category);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("ok", "0123456789012345678901234567890123456789X")]
    public async Task Post_Invalid_Returns400AndStoresNothing(string? body, string? category)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await AddNoteAsync(client, vehicle.Id, body, category);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<ValidationProblemDetails>());
        Assert.Equal(0, (await ListAsync(client, vehicle.Id)).TotalCount);
    }

    [Fact]
    public async Task Post_BodyOver4000Characters_Returns400()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await AddNoteAsync(client, vehicle.Id, new string('a', 4001))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddNoteAsync(client, vehicle.Id, new string('a', 4000))).StatusCode);
    }

    [Fact]
    public async Task List_PagesNewestFirstWithDefaultSizeOfTen()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        for (var i = 1; i <= 12; i++)
        {
            await AddAsync(client, vehicle.Id, $"note {i:D2}");
            await Task.Delay(2);
        }

        var first = await ListAsync(client, vehicle.Id);
        var second = await ListAsync(client, vehicle.Id, "?page=2");

        Assert.Equal(10, first.Items.Count);
        Assert.Equal(12, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal("note 12", first.Items[0].Body);
        Assert.Equal(["note 02", "note 01"], second.Items.Select(n => n.Body));
    }

    [Fact]
    public async Task List_PageBeyondTheLast_ClampsToTheLastPage()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        for (var i = 1; i <= 12; i++)
        {
            await AddAsync(client, vehicle.Id, $"note {i:D2}");
        }

        var page = await ListAsync(client, vehicle.Id, "?page=99");

        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task List_NoNotes_ReturnsAnEmptyFirstPage()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var page = await ListAsync(client, vehicle.Id, "?page=3");

        Assert.Empty(page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=51")]
    [InlineData("?pageSize=0")]
    public async Task List_InvalidPaging_Returns400(string query)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await client.GetAsync($"/api/vehicles/{vehicle.Id}/notes{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdatesBodyAndCategory()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var note = await AddAsync(client, vehicle.Id, "before", "Old");

        var response = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}/notes/{note.Id}", new JournalNoteRequest { Body = "after", Category = null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<JournalNoteDto>();
        Assert.NotNull(updated);
        Assert.Equal("after", updated.Body);
        Assert.Null(updated.Category);
        Assert.Equal(note.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= note.UpdatedAt);
    }

    [Fact]
    public async Task Put_Invalid_Returns400AndKeepsTheNote()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var note = await AddAsync(client, vehicle.Id, "keep");

        var response = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}/notes/{note.Id}", new JournalNoteRequest { Body = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("keep", (await ListAsync(client, vehicle.Id)).Items.Single().Body);
    }

    [Fact]
    public async Task Delete_RemovesTheNote()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var note = await AddAsync(client, vehicle.Id, "bye");

        var response = await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/notes/{note.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, (await ListAsync(client, vehicle.Id)).TotalCount);
    }

    [Fact]
    public async Task UnknownNote_Returns404ProblemDetails()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var put = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}/notes/{Guid.NewGuid()}", new JournalNoteRequest { Body = "x" });
        var delete = await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/notes/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal("application/problem+json", put.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task AnotherUsersVehicle_IsNotFoundForEveryOperation()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var note = await AddAsync(alice, vehicle.Id, "private", "secret");

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/vehicles/{vehicle.Id}/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddNoteAsync(bob, vehicle.Id, "sneaky")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await bob.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}/notes/{note.Id}", new JournalNoteRequest { Body = "hijack" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/vehicles/{vehicle.Id}/notes/{note.Id}")).StatusCode);

        var intact = await ListAsync(alice, vehicle.Id);
        Assert.Equal(["private"], intact.Items.Select(n => n.Body));
        Assert.Equal("secret", intact.Items[0].Category);
    }

    [Fact]
    public async Task Endpoints_WithoutToken_Return401()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/vehicles/{id}/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AddNoteAsync(anonymous, id, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/vehicles/{id}/notes/{id}", new JournalNoteRequest { Body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/vehicles/{id}/notes/{id}")).StatusCode);
    }
}
