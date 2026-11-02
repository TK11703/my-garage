using MyGarage.Shared.Lookups;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Lookups;

public sealed class LookupsApiClient(HttpClient http)
{
    public Task<ApiResult<LookupsDto>> GetAsync(bool includeHidden, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<LookupsDto>(http, HttpMethod.Get, $"api/lookups?includeHidden={(includeHidden ? "true" : "false")}", null, cancellationToken);

    // kind is a route slug from LookupKinds. A rejected name comes back as Invalid (errors) or Conflict (title).
    public Task<ApiResult<LookupItemDto>> AddAsync(string kind, string? name, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<LookupItemDto>(http, HttpMethod.Post, $"api/lookups/{kind}", new AddLookupItemRequest { Name = name }, cancellationToken);

    public Task<ApiResult<LookupItemDto>> SetVisibilityAsync(string kind, Guid itemId, bool isHidden, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<LookupItemDto>(http, HttpMethod.Put, $"api/lookups/{kind}/{itemId}/visibility", new SetLookupVisibilityRequest { IsHidden = isHidden }, cancellationToken);
}
