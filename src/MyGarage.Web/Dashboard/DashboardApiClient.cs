using MyGarage.Shared.Dashboard;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Dashboard;

public sealed class DashboardApiClient(HttpClient http)
{
    public Task<ApiResult<DashboardDto>> GetAsync(CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<DashboardDto>(http, HttpMethod.Get, "api/dashboard", null, cancellationToken);
}
