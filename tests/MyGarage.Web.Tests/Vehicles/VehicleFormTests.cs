using Bunit;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleFormTests : VehicleTestContext
{
    private IRenderedComponent<VehicleForm> RenderForm() =>
        Render<VehicleForm>(parameters => parameters
            .Add(form => form.Model, new VehicleFormModel())
            .Add(form => form.FormName, "create-vehicle")
            .Add(form => form.SubmitText, "Add vehicle")
            .Add(form => form.CancelHref, "/vehicles")
            .Add(form => form.Errors, new Dictionary<string, string[]>())
            .Add(form => form.OnSubmit, () => { }));

    [Theory]
    [InlineData("nickname")]
    [InlineData("make")]
    [InlineData("model")]
    public void RequiredFields_ShowAVisibleMarker(string field)
    {
        var cut = RenderForm();

        var marker = cut.Find($"label[for='{field}'] span.text-danger");
        Assert.Equal("*", marker.TextContent.Trim());
        Assert.Equal("true", marker.GetAttribute("aria-hidden"));
    }

    [Theory]
    [InlineData("trim")]
    [InlineData("color")]
    [InlineData("mileage")]
    [InlineData("doors")]
    public void OptionalFields_HaveNoRequiredMarker(string field)
    {
        var cut = RenderForm();

        Assert.Empty(cut.FindAll($"label[for='{field}'] span.text-danger"));
    }

    [Fact]
    public void ExplainsTheRequiredMarker()
    {
        var cut = RenderForm();

        Assert.Contains("* Required", cut.Find("p.form-text").TextContent);
    }

    [Fact]
    public void IsConstrainedToReadableWidth()
    {
        var cut = RenderForm();

        Assert.NotNull(cut.Find(".row > .col-lg-8 form"));
    }

    [Fact]
    public void SubmitComesBeforeCancel()
    {
        var cut = RenderForm();

        var buttons = cut.FindAll("form > div.d-flex > *");
        Assert.Equal("Add vehicle", buttons[0].TextContent.Trim());
        Assert.Contains("btn-primary", buttons[0].ClassName);
        Assert.Equal("Cancel", buttons[1].TextContent.Trim());
        Assert.Contains("btn-outline-secondary", buttons[1].ClassName);
    }
}
