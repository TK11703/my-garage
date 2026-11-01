using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleCreatePageTests : VehicleTestContext
{
    [Fact]
    public void Form_HasLabeledInputsForEveryField()
    {
        RegisterApi(_ => Json(Detail()));

        var cut = Render<VehicleCreate>();

        foreach (var field in new[] { "nickname", "make", "model", "trim", "color", "mileage", "doors" })
        {
            Assert.NotNull(cut.Find($"input#{field}"));
            Assert.NotNull(cut.Find($"label[for='{field}']"));
        }
    }

    [Fact]
    public void ApiValidationErrors_AreShownPerFieldAndInputsKeepTheirValues()
    {
        RegisterApi(_ => ValidationProblem(("Nickname", "Nickname is required."), ("Mileage", "Mileage must be between 0 and 9,999,999.")));
        var cut = Render<VehicleCreate>();
        cut.Find("input#make").Change("Ford");
        cut.Find("input#model").Change("F-150");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Nickname is required.", cut.Find("#nickname-error").TextContent);
            Assert.Contains("Mileage must be between", cut.Find("#mileage-error").TextContent);
        });
        Assert.Equal("Ford", cut.Find("input#make").GetAttribute("value"));
        Assert.Equal("true", cut.Find("input#nickname").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void Success_PostsTrimmedValuesAndRedirectsToTheNewVehicle()
    {
        var created = Detail(nickname: "Daily");
        RegisterApi(_ => Json(created, System.Net.HttpStatusCode.Created));
        var cut = Render<VehicleCreate>();
        cut.Find("input#nickname").Change("Daily");
        cut.Find("input#make").Change("Ford");
        cut.Find("input#model").Change("F-150");
        cut.Find("input#mileage").Change("1200");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.EndsWith($"/vehicles/{created.Id}", Services.GetRequiredService<NavigationManager>().Uri));
        var request = Requests.Single();
        Assert.Equal(System.Net.Http.HttpMethod.Post, request.Method);
        Assert.Equal("/api/vehicles", request.RequestUri!.AbsolutePath);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Unauthorized)]
    [InlineData(System.Net.HttpStatusCode.Forbidden)]
    public void ApiRejectsToken_ShowsSignInAgain(System.Net.HttpStatusCode status)
    {
        RegisterApi(_ => new HttpResponseMessage(status));
        var cut = Render<VehicleCreate>();

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains(MyGarage.Web.Vehicles.VehicleMessages.NotAccepted, cut.Markup));
    }
}
