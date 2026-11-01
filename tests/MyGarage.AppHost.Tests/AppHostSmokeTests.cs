using System.Net;
using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace MyGarage.AppHost.Tests;

public class AppHostSmokeTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public async Task ApiAndWeb_StartAndReportHealthy()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MyGarage_AppHost>(
            [
                "AzureAd:TenantId=11111111-1111-1111-1111-111111111111",
                "AzureAd:Web:ClientId=22222222-2222-2222-2222-222222222222",
                "AzureAd:Web:ClientSecret=test-secret",
                "AzureAd:Api:ClientId=33333333-3333-3333-3333-333333333333",
                "AzureAd:Api:Scope=access_as_user",
                $"Web:HttpsPort={GetFreePort()}",
            ],
            (options, _) => options.DisableDashboard = true,
            cts.Token);

        await using var app = await appHost.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("postgres", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("garagedb", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("storage", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("blobs", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("web", cts.Token);
    }

    [Fact]
    public async Task WebEndpoint_UsesConfiguredFixedHttpsPort()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);
        var port = GetFreePort();

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MyGarage_AppHost>(
            [
                "AzureAd:TenantId=11111111-1111-1111-1111-111111111111",
                "AzureAd:Web:ClientId=22222222-2222-2222-2222-222222222222",
                "AzureAd:Web:ClientSecret=test-secret",
                "AzureAd:Api:ClientId=33333333-3333-3333-3333-333333333333",
                "AzureAd:Api:Scope=access_as_user",
                $"Web:HttpsPort={port}",
            ],
            (options, _) => options.DisableDashboard = true,
            cts.Token);

        var web = appHost.Resources.Single(resource => resource.Name == "web");
        var endpoint = web.Annotations.OfType<EndpointAnnotation>().Single(annotation => annotation.Name == "https");

        Assert.Equal(port, endpoint.Port);
        Assert.Contains(web.Annotations.OfType<WaitAnnotation>(), annotation => annotation.Resource.Name == "api");
    }
}
