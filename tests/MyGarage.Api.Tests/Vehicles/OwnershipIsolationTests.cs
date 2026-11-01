using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

// SC-003 / SC-010: every endpoint treats another user's data as non-existent and changes nothing.
public class OwnershipIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[16]];

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Seed(Guid VehicleId, Guid TagId, Guid NoteId, Guid ImageId);

    private sealed record Probe(string Name, Func<HttpClient, Seed, Task<HttpResponseMessage>> Send);

    private static readonly Probe[] Probes =
    [
        new("GET vehicle", (c, s) => c.GetAsync($"/api/vehicles/{s.VehicleId}")),
        new("PUT vehicle", (c, s) => c.PutAsJsonAsync($"/api/vehicles/{s.VehicleId}", new VehicleRequest { Nickname = "x", Make = "x", Model = "x" })),
        new("DELETE vehicle", (c, s) => c.DeleteAsync($"/api/vehicles/{s.VehicleId}")),
        new("POST tag", (c, s) => c.PostAsJsonAsync($"/api/vehicles/{s.VehicleId}/tags", new AddVehicleTagRequest { Name = "x" })),
        new("DELETE tag", (c, s) => c.DeleteAsync($"/api/vehicles/{s.VehicleId}/tags/{s.TagId}")),
        new("GET notes", (c, s) => c.GetAsync($"/api/vehicles/{s.VehicleId}/notes")),
        new("POST note", (c, s) => c.PostAsJsonAsync($"/api/vehicles/{s.VehicleId}/notes", new JournalNoteRequest { Body = "x" })),
        new("PUT note", (c, s) => c.PutAsJsonAsync($"/api/vehicles/{s.VehicleId}/notes/{s.NoteId}", new JournalNoteRequest { Body = "x" })),
        new("DELETE note", (c, s) => c.DeleteAsync($"/api/vehicles/{s.VehicleId}/notes/{s.NoteId}")),
        new("POST image", (c, s) => c.PostAsync($"/api/vehicles/{s.VehicleId}/images", new MultipartFormDataContent { { new ByteArrayContent(Png), "file", "x.png" } })),
        new("GET image content", (c, s) => c.GetAsync($"/api/vehicles/{s.VehicleId}/images/{s.ImageId}/content")),
        new("PUT main image", (c, s) => c.PutAsync($"/api/vehicles/{s.VehicleId}/images/{s.ImageId}/main", null)),
        new("DELETE image", (c, s) => c.DeleteAsync($"/api/vehicles/{s.VehicleId}/images/{s.ImageId}")),
    ];

    private async Task<Seed> SeedAsync(HttpClient owner)
    {
        var vehicle = (await (await owner.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Nickname = "Private car", Make = "Ford", Model = "F-150" }))
            .Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        var tag = (await (await owner.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/tags", new AddVehicleTagRequest { Name = "private-tag" }))
            .Content.ReadFromJsonAsync<VehicleTagDto>())!;
        var note = (await (await owner.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/notes", new JournalNoteRequest { Body = "private note", Category = "secret" }))
            .Content.ReadFromJsonAsync<JournalNoteDto>())!;
        var image = (await (await owner.PostAsync($"/api/vehicles/{vehicle.Id}/images", new MultipartFormDataContent { { new ByteArrayContent(Png), "file", "a.png" } }))
            .Content.ReadFromJsonAsync<VehicleImageDto>())!;
        return new Seed(vehicle.Id, tag.Id, note.Id, image.Id);
    }

    [Fact]
    public async Task AnotherUser_GetsNotFoundForEveryEndpointAndChangesNothing()
    {
        var alice = factory.CreateClientForUser("alice-oid");
        var bob = factory.CreateClientForUser("bob-oid");
        var seed = await SeedAsync(alice);
        var before = await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{seed.VehicleId}");

        var failures = new List<string>();
        foreach (var probe in Probes)
        {
            var response = await probe.Send(bob, seed);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                failures.Add($"{probe.Name}: expected 404 but got {(int)response.StatusCode}");
            }
        }

        Assert.Empty(failures);

        var after = await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{seed.VehicleId}");
        Assert.Equal(before!.Nickname, after!.Nickname);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal([seed.TagId], after.Tags.Select(t => t.Id));
        Assert.Equal([seed.ImageId], after.Images.Select(i => i.Id));
        Assert.True(after.Images.Single().IsMain);
        var notes = await alice.GetFromJsonAsync<PagedResultDto<JournalNoteDto>>($"/api/vehicles/{seed.VehicleId}/notes");
        Assert.Equal(["private note"], notes!.Items.Select(n => n.Body));
        Assert.Equal("secret", notes.Items.Single().Category);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/vehicles/{seed.VehicleId}/images/{seed.ImageId}/content")).StatusCode);
    }

    [Fact]
    public async Task AnotherUser_NeverSeesTheOwnersDataInListsSearchOrFilterOptions()
    {
        var alice = factory.CreateClientForUser("alice-oid");
        var bob = factory.CreateClientForUser("bob-oid");
        await SeedAsync(alice);

        var list = await bob.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles");
        var searched = await bob.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles?q=private&tag=private-tag&make=ford");
        var options = await bob.GetFromJsonAsync<VehicleFilterOptionsDto>("/api/vehicles/filter-options");

        Assert.Empty(list!.Items);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(searched!.Items);
        Assert.Empty(options!.Makes);
        Assert.Empty(options.Colors);
        Assert.Empty(options.Tags);
    }

    [Fact]
    public async Task ARequestThatNamesAnotherOwner_IsStillScopedToTheCaller()
    {
        var alice = factory.CreateClientForUser("alice-oid");
        var bob = factory.CreateClientForUser("bob-oid");
        var seed = await SeedAsync(alice);

        var response = await bob.GetAsync($"/api/vehicles/{seed.VehicleId}?ownerId=alice-oid&userId=alice-oid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
