using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.Inspections;
using MyGarage.Web.Drafts;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Drafts;

public class DraftReviewPageTests : DraftTestContext
{
    private const string FormSelector = "form[data-testid='review-form']";

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private IRenderedComponent<DraftReviewPage> RenderPage(MaintenanceDraftDto? draft, HttpContext? post = null)
    {
        CurrentDraft = draft;
        RegisterDrafts();
        return Render<DraftReviewPage>(parameters =>
        {
            parameters.Add(page => page.DraftId, draft?.Id ?? Guid.NewGuid());
            if (post is not null)
            {
                parameters.AddCascadingValue<HttpContext>(post);
            }
        });
    }

    private static DraftProposal InspectionDraft() =>
        new() { Kind = DraftKind.Inspection, Inspection = new InspectionProposal { Type = InspectionType.Safety, Date = new DateOnly(2026, 9, 1), Cost = 25m } };

    [Fact]
    public void ShowsTheTitleAndTrail()
    {
        var cut = RenderPage(DraftDto());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Review draft", cut.Find("h1").TextContent);
            var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
            Assert.Equal(["Home", "Maintenance Drafts", "Review draft"], items.Select(item => item.TextContent.Trim()));
            Assert.Equal("/drafts", items[1].QuerySelector("a")!.GetAttribute("href"));
        });
    }

    [Fact]
    public void ARequestForTheDraft_GoesToTheApi()
    {
        var draft = DraftDto();

        var cut = RenderPage(draft);

        cut.WaitForElement(FormSelector);
        Assert.Contains(Calls, call => call.Method == HttpMethod.Get && call.Path == $"/api/drafts/{draft.Id}");
    }

    // Original ---------------------------------------------------------------------------------------------------

    [Fact]
    public void APdf_IsShownInAnUnsandboxedTitledFrame_WithADownloadLink()
    {
        var draft = DraftDto(contentType: "application/pdf");

        var cut = RenderPage(draft);

        cut.WaitForAssertion(() =>
        {
            var frame = cut.Find("iframe[data-testid='original-pdf']");
            Assert.Equal($"/drafts/{draft.Id}/attachment", frame.GetAttribute("src"));
            Assert.False(string.IsNullOrWhiteSpace(frame.GetAttribute("title")));
            // Chrome's PDF viewer does not render inside a sandboxed frame.
            Assert.False(frame.HasAttribute("sandbox"));
            Assert.Empty(cut.FindAll("img[data-testid='original-image']"));

            var download = cut.Find("a[data-testid='download-original']");
            Assert.Equal($"/drafts/{draft.Id}/attachment", download.GetAttribute("href"));
            Assert.True(download.HasAttribute("download"));
        });
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public void AnImage_IsShownAsAnImageWithAlternativeText(string contentType)
    {
        var draft = DraftDto(contentType: contentType);

        var cut = RenderPage(draft);

        cut.WaitForAssertion(() =>
        {
            var image = cut.Find("img[data-testid='original-image']");
            Assert.Equal($"/drafts/{draft.Id}/attachment", image.GetAttribute("src"));
            Assert.False(string.IsNullOrWhiteSpace(image.GetAttribute("alt")));
            Assert.Empty(cut.FindAll("iframe"));
        });
    }

    [Fact]
    public void ADraftReadFromTheEmailText_HasNoFileAndNoKeepOriginalChoice()
    {
        var cut = RenderPage(DraftDto(hasAttachment: false));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("iframe"));
            Assert.Empty(cut.FindAll("img[data-testid='original-image']"));
            Assert.Empty(cut.FindAll("a[data-testid='download-original']"));
            Assert.Contains("read from the text of the email", cut.Find("[data-testid='text-source-note']").TextContent);
            Assert.Empty(cut.FindAll("input#keep-original"));
        });
    }

    [Fact]
    public void TheEmailDetails_AreShown_AndEncoded()
    {
        var cut = RenderPage(DraftDto(sender: "<b>x</b>@example.com", subject: "<script>alert(1)</script>"));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("b"));
            Assert.Empty(cut.FindAll("script"));
            Assert.Equal("<b>x</b>@example.com", cut.Find("[data-testid='email-sender']").TextContent.Trim());
            Assert.Equal("<script>alert(1)</script>", cut.Find("[data-testid='email-subject']").TextContent.Trim());
            Assert.Equal("Nov 3, 2026 at 3:30 PM UTC", cut.Find("[data-testid='email-arrived']").TextContent.Trim());
        });
    }

    // Record type -------------------------------------------------------------------------------------------------

    [Fact]
    public void TheRecordTypeSelect_ListsTheSevenKinds_WithTheDraftsKindChosen()
    {
        var cut = RenderPage(DraftDto(kind: DraftKind.Inspection, proposal: InspectionDraft()));

        cut.WaitForAssertion(() =>
        {
            var select = cut.Find("select#kind");
            Assert.Equal("Model.KindValue", select.GetAttribute("name"));
            Assert.NotNull(cut.Find("label[for='kind']"));
            Assert.Equal(
                ["Choose a record type", "Service record", "Fuel", "Charge", "Inspection", "Registration", "Insurance", "Tax"],
                select.QuerySelectorAll("option").Select(option => option.TextContent.Trim()));
            Assert.Equal("inspection", select.QuerySelector("option[selected]")!.GetAttribute("value"));
        });
    }

    [Fact]
    public void ChangingTheRecordType_CallsTheKindApi_AndShowsTheFormForTheNewKind()
    {
        var draft = DraftDto();
        KindAnswer = _ => Json(DraftDto(draft.Id, kind: DraftKind.Inspection, proposal: InspectionDraft()));
        var cut = RenderPage(draft, Post("change-kind"));
        cut.WaitForElement("select#kind").Change("inspection");

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() =>
        {
            var put = Assert.Single(Calls, call => call.Method == HttpMethod.Put);
            Assert.Equal($"/api/drafts/{draft.Id}/kind", put.Path);
            Assert.Equal("inspection", JsonDocument.Parse(put.Body).RootElement.GetProperty("kind").GetString());

            Assert.NotNull(cut.Find("#inspection-type"));
            Assert.Equal("2026-09-01", cut.Find("#inspection-date").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='service-editor']"));
        });
    }

    [Fact]
    public void ChangingTheRecordType_KeepsTheChosenVehicle()
    {
        var draft = DraftDto(vehicleId: DefaultVehicleId);
        KindAnswer = _ => Json(DraftDto(draft.Id, kind: DraftKind.Inspection, proposal: InspectionDraft(), vehicleId: DefaultVehicleId));
        var cut = RenderPage(draft, Post("change-kind"));
        cut.WaitForElement("select#vehicle").Change(OtherVehicleId.ToString());
        cut.Find("select#kind").Change("inspection");

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#inspection-type")));
        Assert.Equal(OtherVehicleId.ToString(), cut.Find("select#vehicle option[selected]").GetAttribute("value"));
    }

    [Fact]
    public void AnUnchosenRecordType_IsSentAsNoKind()
    {
        var draft = DraftDto();
        var cut = RenderPage(draft, Post("change-kind"));
        cut.WaitForElement("select#kind").Change(string.Empty);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() => Assert.Equal(string.Empty, JsonDocument.Parse(Assert.Single(Calls, call => call.Method == HttpMethod.Put).Body).RootElement.GetProperty("kind").ToString()));
    }

    // Vehicle -----------------------------------------------------------------------------------------------------

    [Fact]
    public void TheVehiclePicker_ListsOnlyTheVehiclesTheApiReturned_PreselectingTheSuggestion()
    {
        var cut = RenderPage(DraftDto(match: VehicleMatch.Vin));

        cut.WaitForAssertion(() =>
        {
            var select = cut.Find("select#vehicle");
            Assert.Equal("Model.VehicleId", select.GetAttribute("name"));
            Assert.NotNull(cut.Find("label[for='vehicle']"));
            Assert.Equal(
                ["Choose a vehicle", "2020 Ford F-150", "2019 Subaru Outback"],
                select.QuerySelectorAll("option").Select(option => option.TextContent.Trim()));
            Assert.Equal(DefaultVehicleId.ToString(), select.QuerySelector("option[selected]")!.GetAttribute("value"));
            Assert.Equal("Matched by VIN.", cut.Find("[data-testid='vehicle-match']").TextContent.Trim());
        });
    }

    [Theory]
    [InlineData(VehicleMatch.Description, "Matched by year, make, and model.")]
    [InlineData(VehicleMatch.OnlyVehicle, "This is your only vehicle.")]
    public void TheMatchNote_SaysHowTheVehicleWasFound(VehicleMatch match, string note)
    {
        var cut = RenderPage(DraftDto(match: match));

        cut.WaitForAssertion(() => Assert.Equal(note, cut.Find("[data-testid='vehicle-match']").TextContent.Trim()));
    }

    [Fact]
    public void WithoutAMatch_ThePickerStartsOnChooseAVehicle()
    {
        var cut = RenderPage(DraftDto(withoutVehicle: true));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Choose a vehicle.", cut.Find("[data-testid='vehicle-match']").TextContent.Trim());
            Assert.Empty(cut.FindAll("select#vehicle option[selected]:not([value=''])"));
        });
    }

    [Fact]
    public void VehicleNames_AreEncoded()
    {
        var cut = RenderPage(DraftDto(candidates: [new VehicleSuggestionDto { VehicleId = DefaultVehicleId, Name = "<b>Truck</b>", Match = VehicleMatch.Vin }]));

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("b"));
            Assert.Contains("<b>Truck</b>", cut.Find("select#vehicle").TextContent);
        });
    }

    // Compatibility, check, and save -----------------------------------------------------------------------------

    [Fact]
    public void TheCompatibilityPanel_ShowsTheDraftsResult()
    {
        var cut = RenderPage(DraftDto(compatibility: Acceptable(DraftKind.ServiceRecord, "No line items were found.")));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Ready to save", cut.Find("[data-testid='compatibility-summary']").TextContent.Trim());
            Assert.Single(cut.FindAll("[data-testid='compatibility-warning']"));
        });
    }

    [Fact]
    public void Save_IsEnabled_WhileTheProposalIsAcceptable()
    {
        var cut = RenderPage(DraftDto(compatibility: Acceptable()));

        cut.WaitForAssertion(() => Assert.False(cut.Find("button[data-testid='save-record']").HasAttribute("disabled")));
    }

    [Fact]
    public void Save_IsDisabled_WhileTheProposalIsNotAcceptable()
    {
        var cut = RenderPage(DraftDto(compatibility: NeedsAttention(DraftKind.ServiceRecord, ("serviceRecord.odometerReading", CompatibilityStatus.Missing, "Required"))));

        cut.WaitForAssertion(() =>
        {
            var save = cut.Find("button[data-testid='save-record']");
            Assert.True(save.HasAttribute("disabled"));
            Assert.Equal("Save record", save.TextContent.Trim());
            Assert.Equal("save", save.GetAttribute("value"));
            Assert.Equal("action", save.GetAttribute("name"));
        });
    }

    [Fact]
    public void CheckAgain_SendsTheEditedProposal_AndShowsTheNewResult()
    {
        var draft = DraftDto();
        var cut = RenderPage(draft, Post("check"));
        CompatibilityAnswer = NeedsAttention(DraftKind.ServiceRecord, ("serviceRecord.odometerReading", CompatibilityStatus.Invalid, "Odometer reading must be between 0 and 9,999,999."));
        cut.WaitForElement("#serviceRecord-odometerReading").Change("46000");

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() =>
        {
            var body = LastBodyTo($"/api/drafts/{draft.Id}/compatibility", HttpMethod.Post);
            Assert.Equal("service_record", body.GetProperty("kind").GetString());
            Assert.Equal(46000, body.GetProperty("serviceRecord").GetProperty("odometerReading").GetInt32());
            Assert.Equal("1 thing needs attention", cut.Find("[data-testid='compatibility-summary']").TextContent.Trim());
            Assert.Contains("must be between 0 and 9,999,999", cut.Find("#serviceRecord-odometerReading-error").TextContent);
            Assert.True(cut.Find("button[data-testid='save-record']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void WithNoPostedButton_TheFormJustChecks_NeverSaves()
    {
        var cut = RenderPage(DraftDto());
        cut.WaitForElement(FormSelector);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() => Assert.Contains(Calls, call => call.Path.EndsWith("/compatibility", StringComparison.Ordinal)));
        Assert.DoesNotContain(Calls, call => call.Path.EndsWith("/accept", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_PostsTheVehicleAndProposal_ThenReturnsToTheVehiclesMaintenanceTab()
    {
        var draft = DraftDto();
        AcceptAnswer = _ => Json(new AcceptedDraftDto { Kind = DraftKind.ServiceRecord, VehicleId = OtherVehicleId, RecordId = Guid.NewGuid() }, HttpStatusCode.Created);
        var cut = RenderPage(draft, Post("save"));
        cut.WaitForElement("select#vehicle").Change(OtherVehicleId.ToString());
        cut.Find("#serviceRecord-shopOrPerson").Change("Quick Lube");

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{OtherVehicleId}?tab=service-records&saved=draft", CurrentUri));
        var body = LastBodyTo($"/api/drafts/{draft.Id}/accept", HttpMethod.Post);
        Assert.Equal(OtherVehicleId, body.GetProperty("vehicleId").GetGuid());
        Assert.False(body.GetProperty("keepOriginal").GetBoolean());
        var section = body.GetProperty("proposal").GetProperty("serviceRecord");
        Assert.Equal("Quick Lube", section.GetProperty("shopOrPerson").GetString());
        Assert.Equal(2, section.GetProperty("lineItems").GetArrayLength());
        Assert.Equal("service_record", body.GetProperty("proposal").GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData(DraftKind.Fuel, "&section=fuel")]
    [InlineData(DraftKind.Inspection, "&section=compliance")]
    public void Save_OpensTheAccordionItemThatHoldsTheNewRecord(DraftKind kind, string section)
    {
        var draft = DraftDto(kind: kind, proposal: new DraftProposal { Kind = kind }, compatibility: Acceptable(kind));
        AcceptAnswer = _ => Json(new AcceptedDraftDto { Kind = kind, VehicleId = DefaultVehicleId, RecordId = Guid.NewGuid() }, HttpStatusCode.Created);
        var cut = RenderPage(draft, Post("save"));
        cut.WaitForElement(FormSelector);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{DefaultVehicleId}?tab=service-records{section}&saved=draft", CurrentUri));
    }

    [Fact]
    public void Save_WithoutAVehicle_AsksForOne_AndSendsNothing()
    {
        var cut = RenderPage(DraftDto(withoutVehicle: true), Post("save"));
        cut.WaitForElement(FormSelector);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Choose a vehicle.", cut.Find("#vehicle-error").TextContent.Trim());
            Assert.Equal("vehicle-error", cut.Find("select#vehicle").GetAttribute("aria-describedby"));
        });
        Assert.DoesNotContain(Calls, call => call.Path.EndsWith("/accept", StringComparison.Ordinal));
    }

    [Fact]
    public void ARejectedSave_ShowsTheMessagesBesideTheFields_AndAnnouncesIt()
    {
        var draft = DraftDto();
        AcceptAnswer = _ => ValidationProblem(("Proposal.ServiceRecord.OdometerReading", "Odometer reading is required."));
        CompatibilityAnswer = NeedsAttention(DraftKind.ServiceRecord, ("serviceRecord.odometerReading", CompatibilityStatus.Missing, "Odometer reading is required."));
        var cut = RenderPage(draft, Post("save"));
        cut.WaitForElement(FormSelector);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Odometer reading is required.", cut.Find("#serviceRecord-odometerReading-error").TextContent);
            Assert.NotNull(cut.Find("[data-testid='compatibility-save-failed']"));
            Assert.DoesNotContain("/vehicles/", CurrentUri);
        });
    }

    [Fact]
    public void KeepOriginal_IsOfferedOnlyWithAnAttachment_AndSentWhenChecked()
    {
        var draft = DraftDto();
        var cut = RenderPage(draft, Post("save"));
        var box = cut.WaitForElement("input#keep-original");
        Assert.Equal("checkbox", box.GetAttribute("type"));
        Assert.False(box.HasAttribute("checked"));
        Assert.Equal("Model.KeepOriginal", box.GetAttribute("name"));
        Assert.NotNull(cut.Find("label[for='keep-original']"));
        Assert.Contains("Documents", cut.Find("#keep-original-help").TextContent);

        box.Change(true);
        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() => Assert.True(LastBodyTo($"/api/drafts/{draft.Id}/accept", HttpMethod.Post).GetProperty("keepOriginal").GetBoolean()));
    }

    // Line items and parts ----------------------------------------------------------------------------------------

    [Fact]
    public void AddingALineItem_AddsABlankLine_AndRechecks()
    {
        var cut = RenderPage(DraftDto(), Post("add-line"));
        cut.WaitForElement(FormSelector);
        Assert.Equal(2, cut.FindAll("[data-testid='line-item']").Count);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, cut.FindAll("[data-testid='line-item']").Count);
            Assert.Contains(Calls, call => call.Path.EndsWith("/compatibility", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void RemovingALineItem_DropsThatLine()
    {
        var cut = RenderPage(DraftDto(), Post("remove-line:0"));
        cut.WaitForElement(FormSelector);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() =>
        {
            var lines = cut.FindAll("[data-testid='line-item']");
            Assert.Single(lines);
            Assert.Equal("Brake job", lines[0].QuerySelector("input[id$='-description']")!.GetAttribute("value"));
        });
    }

    [Fact]
    public void AddingAPart_AddsARowToThatLine()
    {
        var cut = RenderPage(DraftDto(), Post("add-part:1"));
        cut.WaitForElement(FormSelector);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='line-item']")[1].QuerySelectorAll("[data-testid='part-row']").Count));
    }

    [Fact]
    public void RemovingAPart_DropsThatRow()
    {
        var cut = RenderPage(DraftDto(), Post("remove-part:1:0"));
        cut.WaitForElement(FormSelector);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='line-item']")[1].QuerySelectorAll("[data-testid='part-row']")));
    }

    [Theory]
    [InlineData("remove-line:abc")]
    [InlineData("remove-line:99")]
    [InlineData("add-part:-1")]
    [InlineData("remove-part:1")]
    [InlineData("bogus")]
    public void ABadAction_ChangesNothing(string action)
    {
        var cut = RenderPage(DraftDto(), Post(action));
        cut.WaitForElement(FormSelector);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid='line-item']").Count));
        Assert.DoesNotContain(Calls, call => call.Path.EndsWith("/accept", StringComparison.Ordinal));
    }

    // Other kinds -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(DraftKind.Fuel, "fuelEnergy-date")]
    [InlineData(DraftKind.Energy, "fuelEnergy-quantity")]
    [InlineData(DraftKind.Inspection, "inspection-type")]
    [InlineData(DraftKind.Registration, "registration-state")]
    [InlineData(DraftKind.Insurance, "insurance-duration")]
    [InlineData(DraftKind.Tax, "tax-recurrence")]
    public void EachKind_ShowsItsOwnForm(DraftKind kind, string fieldId)
    {
        var cut = RenderPage(DraftDto(kind: kind, proposal: new DraftProposal { Kind = kind }));

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find($"#{fieldId}"));
            Assert.NotNull(cut.Find($"label[for='{fieldId}']"));
            Assert.Empty(cut.FindAll("[data-testid='service-editor']"));
        });
    }

    // Draft states ------------------------------------------------------------------------------------------------

    [Fact]
    public void AManualEntryDraft_AsksForARecordType_AndCannotBeSavedYet()
    {
        var draft = DraftDto(
            state: DraftState.ManualEntry,
            kind: null,
            proposal: new DraftProposal(),
            notes: "We could not read this file.",
            compatibility: NeedsAttention(null, ("kind", CompatibilityStatus.Missing, "Choose a record type.")));

        var cut = RenderPage(draft);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("We could not read this file.", cut.Find("[data-testid='draft-notes']").TextContent);
            Assert.Equal(string.Empty, cut.Find("select#kind").QuerySelector("option[selected]")?.GetAttribute("value") ?? string.Empty);
            Assert.Empty(cut.FindAll("[data-testid='service-editor']"));
            Assert.True(cut.Find("button[data-testid='save-record']").HasAttribute("disabled"));
            Assert.Equal("#kind", cut.Find("[data-testid='compatibility-field'] a").GetAttribute("href"));
        });
    }

    [Fact]
    public void AManualEntryDraft_ShowsTheOriginalAndTheTypeChooser_WithNoFormUntilATypeIsChosen()
    {
        var draft = DraftDto(
            state: DraftState.ManualEntry,
            kind: null,
            proposal: new DraftProposal(),
            compatibility: NeedsAttention(null, ("kind", CompatibilityStatus.Missing, "Choose a record type.")));

        var cut = RenderPage(draft);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"/drafts/{draft.Id}/attachment", cut.Find("iframe[data-testid='original-pdf']").GetAttribute("src"));
            Assert.NotNull(cut.Find("a[data-testid='download-original']"));
            Assert.Equal("Choose a record type", cut.Find("select#kind option[selected]").TextContent.Trim());
            Assert.Contains("Choose a record type above to enter the details.", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid='service-editor']"));
            Assert.Empty(cut.FindAll("#inspection-type, #fuelEnergy-date, #registration-state, #insurance-duration, #tax-recurrence"));
        });
    }

    [Fact]
    public void ChoosingATypeForAManualEntryDraft_ShowsTheEmptyForm_WithEveryRequiredFieldMissing()
    {
        var draft = DraftDto(
            state: DraftState.ManualEntry,
            kind: null,
            proposal: new DraftProposal(),
            compatibility: NeedsAttention(null, ("kind", CompatibilityStatus.Missing, "Choose a record type.")));
        KindAnswer = _ => Json(DraftDto(
            draft.Id,
            kind: DraftKind.Inspection,
            proposal: new DraftProposal { Kind = DraftKind.Inspection, Inspection = new InspectionProposal() },
            compatibility: NeedsAttention(
                DraftKind.Inspection,
                ("inspection.type", CompatibilityStatus.Missing, "Type is required."),
                ("inspection.date", CompatibilityStatus.Missing, "Date is required."))));
        var cut = RenderPage(draft, Post("change-kind"));
        cut.WaitForElement("select#kind").Change("inspection");

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() =>
        {
            var put = Assert.Single(Calls, call => call.Method == HttpMethod.Put);
            Assert.Equal($"/api/drafts/{draft.Id}/kind", put.Path);
            Assert.Equal("inspection", JsonDocument.Parse(put.Body).RootElement.GetProperty("kind").GetString());

            Assert.Equal(string.Empty, cut.Find("#inspection-date").GetAttribute("value") ?? string.Empty);
            Assert.Equal("inspection", cut.Find("select#kind option[selected]").GetAttribute("value"));
            Assert.Equal(["missing", "missing"], cut.FindAll("[data-testid='compatibility-field']").Select(field => field.GetAttribute("data-status")));
            Assert.Equal("2 things need attention", cut.Find("[data-testid='compatibility-summary']").TextContent.Trim());
            Assert.True(cut.Find("button[data-testid='save-record']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void TheNotesAboutSkippedAttachments_AreShownEscaped()
    {
        var draft = DraftDto(notes: "<b>Read me</b> 1 attachment was skipped: unsupported type");

        var cut = RenderPage(draft);

        cut.WaitForAssertion(() =>
        {
            var notes = cut.Find("[data-testid='draft-notes']");
            Assert.Equal("<b>Read me</b> 1 attachment was skipped: unsupported type", notes.TextContent);
            Assert.Empty(notes.QuerySelectorAll("b"));
        });
    }

    [Theory]
    [InlineData(DraftState.Included, "already included")]
    [InlineData(DraftState.Discarded, "was discarded")]
    [InlineData(DraftState.Expired, "has expired")]
    public void ADecidedOrExpiredDraft_IsReadOnly(DraftState state, string wording)
    {
        var cut = RenderPage(DraftDto(state: state, kind: null, proposal: new DraftProposal(), hasAttachment: false));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(wording, cut.Find("[data-testid='draft-decided']").TextContent);
            Assert.Equal("/drafts", cut.Find("[data-testid='draft-decided'] a").GetAttribute("href"));
            Assert.Empty(cut.FindAll("form"));
            Assert.Empty(cut.FindAll("button[type='submit']"));
        });
    }

    [Fact]
    public void AlreadyDecidedWhileSaving_ShowsTheReadOnlyNotice()
    {
        AcceptAnswer = _ => Problem(HttpStatusCode.Conflict, "This draft was already decided.");
        var cut = RenderPage(DraftDto(), Post("save"));
        cut.WaitForElement(FormSelector);

        cut.Find(FormSelector).Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("already decided", cut.Find("[data-testid='draft-decided']").TextContent);
            Assert.Empty(cut.FindAll(FormSelector));
        });
    }

    [Fact]
    public void AnUnknownDraft_ShowsTheNotFoundPage_RevealingNothing()
    {
        var cut = RenderPage(null);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Draft not found", cut.Find("h1").TextContent);
            Assert.Contains("could not be found", cut.Markup);
            Assert.Equal("/drafts", cut.Find("a.btn").GetAttribute("href"));
            Assert.Empty(cut.FindAll("form"));
        });
    }

    [Fact]
    public void AServerError_ShowsTheFailureAlert()
    {
        DraftFailure = HttpStatusCode.InternalServerError;
        RegisterDrafts();

        var cut = Render<DraftReviewPage>(parameters => parameters.Add(page => page.DraftId, Guid.NewGuid()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(VehicleMessages.Unavailable, cut.Find("[role='alert']").TextContent);
            Assert.Empty(cut.FindAll("form"));
        });
    }

    [Fact]
    public void ARejectedSignIn_ShowsTheSignInAgainAction()
    {
        DraftFailure = HttpStatusCode.Unauthorized;
        RegisterDrafts();
        var id = Guid.NewGuid();

        var cut = Render<DraftReviewPage>(parameters => parameters.Add(page => page.DraftId, id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(VehicleMessages.NotAccepted, cut.Markup);
            Assert.Equal($"/authentication/login?returnUrl={Uri.EscapeDataString($"/drafts/{id}")}", cut.Find("a.btn-primary").GetAttribute("href"));
        });
    }

    // Discard -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Discard_AsksForConfirmationInline_AndSendsNothingUntilConfirmed()
    {
        var cut = RenderPage(DraftDto());

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("details[data-testid='discard-draft']");
            Assert.False(section.HasAttribute("open"));
            Assert.Contains("Discard this draft? The email's file and text are removed and this cannot be undone.", section.TextContent);
            Assert.Equal("Yes, discard this draft", section.QuerySelector("button[type='submit']")!.TextContent.Trim());
            Assert.Equal("Keep draft", section.QuerySelector("a.btn")!.TextContent.Trim());
            Assert.Contains("alert-danger", section.QuerySelector(".alert")!.ClassList);
            Assert.Equal("alert", section.QuerySelector(".alert")!.GetAttribute("role"));
            Assert.Contains("btn-outline-danger", section.QuerySelector("summary")!.ClassList);
        });
        Assert.DoesNotContain(Calls, call => call.Path.EndsWith("/discard", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfirmingDiscard_CallsTheApi_AndReturnsToTheList()
    {
        var draft = DraftDto();
        var cut = RenderPage(draft);
        cut.WaitForElement("details[data-testid='discard-draft'] form");

        cut.Find("details[data-testid='discard-draft'] form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith("/drafts", CurrentUri));
        Assert.Contains(Calls, call => call.Method == HttpMethod.Post && call.Path == $"/api/drafts/{draft.Id}/discard");
    }

    [Fact]
    public void DiscardingADraftThatWasAlreadyDecided_ShowsTheReadOnlyNotice()
    {
        DiscardAnswer = _ => Problem(HttpStatusCode.Conflict, "This draft was already decided.");
        var cut = RenderPage(DraftDto());
        cut.WaitForElement("details[data-testid='discard-draft'] form");

        cut.Find("details[data-testid='discard-draft'] form").Submit();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='draft-decided']")));
    }
}
