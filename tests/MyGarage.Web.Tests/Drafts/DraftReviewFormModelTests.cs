using MyGarage.Shared.Drafts;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Taxes;
using MyGarage.Web.Drafts;
using MyGarage.Web.Maintenance;
using MyGarage.Web.ServiceRecords;

namespace MyGarage.Web.Tests.Drafts;

public class DraftReviewFormModelTests
{
    private static DraftReviewFormModel ServiceModel() =>
        DraftReviewFormModel.From(
            new DraftProposal
            {
                Kind = DraftKind.ServiceRecord,
                ServiceRecord = new ServiceRecordProposal
                {
                    DateServiced = new DateOnly(2026, 10, 12),
                    OdometerReading = 45_000,
                    ShopOrPerson = "Joe's",
                    DocumentTotal = 200m,
                    LineItems =
                    [
                        new ServiceLineItemRequest { Description = "Oil", FlatCost = 40m },
                        new ServiceLineItemRequest { Description = "Brakes", LaborHours = 1m, LaborRate = 100m, Parts = [new ServicePartRequest { Name = "Pad", Quantity = 2m, UnitPrice = 10m }] },
                    ],
                },
            },
            DraftKind.ServiceRecord,
            Guid.NewGuid(),
            keepOriginal: false);

    [Fact]
    public void AServiceRecordProposal_RoundTripsThroughTheForm()
    {
        var model = ServiceModel();

        var proposal = model.ToProposal();

        Assert.Equal(DraftKind.ServiceRecord, proposal.Kind);
        var section = proposal.ServiceRecord!;
        Assert.Equal(new DateOnly(2026, 10, 12), section.DateServiced);
        Assert.Equal(45_000, section.OdometerReading);
        Assert.Equal(200m, section.DocumentTotal);
        Assert.Equal(40m, section.LineItems[0].FlatCost);
        Assert.Null(section.LineItems[0].LaborHours);
        Assert.Null(section.LineItems[1].FlatCost);
        Assert.Equal(100m, section.LineItems[1].LaborRate);
        Assert.Equal("Pad", Assert.Single(section.LineItems[1].Parts).Name);
        Assert.Null(proposal.FuelEnergy);
        Assert.Null(proposal.Inspection);
    }

    [Fact]
    public void ALineWithBothFlatCostAndLabor_IsShownAsFlat()
    {
        var line = ProposalLineForm.From(new ServiceLineItemRequest { Description = "x", FlatCost = 5m, LaborHours = 1m, LaborRate = 2m });

        Assert.Equal(PricingMode.Flat, line.Pricing);
        Assert.Null(line.ToRequest().LaborHours);
    }

    [Fact]
    public void Totals_UseTheChosenPricingMethod_AndRoundEachProductToCents()
    {
        var model = ServiceModel();

        Assert.Equal(40m, model.Service.Lines[0].Total);
        Assert.Equal(120m, model.Service.Lines[1].Total);
        Assert.Equal(160m, model.Service.Total);

        model.Service.Lines[1].Parts.Add(new ServicePartRow { Name = "Odd", Quantity = 0.333m, UnitPrice = 1m });
        Assert.Equal(120.33m, model.Service.Lines[1].Total);

        model.Service.Lines[1].Pricing = PricingMode.Flat;
        model.Service.Lines[1].FlatCost = null;
        Assert.Equal(0m, model.Service.Lines[1].Total);
    }

    [Fact]
    public void AddingAndRemovingLinesAndParts_KeepsTheRowsInOrder()
    {
        var model = ServiceModel();

        Assert.True(model.Service.AddLine());
        Assert.Equal(3, model.Service.Lines.Count);
        Assert.True(model.Service.AddPart(1));
        Assert.Equal(2, model.Service.Lines[1].Parts.Count);

        model.Service.RemovePart(1, 0);
        Assert.Single(model.Service.Lines[1].Parts);
        Assert.Null(model.Service.Lines[1].Parts[0].Name);

        model.Service.RemoveLine(0);
        Assert.Equal(["Brakes", null], model.Service.Lines.Select(line => line.Description));
    }

    [Fact]
    public void OutOfRangeAddsAndRemoves_AreIgnored()
    {
        var model = ServiceModel();

        model.Service.RemoveLine(9);
        model.Service.RemoveLine(-1);
        model.Service.RemovePart(9, 0);
        model.Service.RemovePart(1, 9);

        Assert.False(model.Service.AddPart(9));
        Assert.Equal(2, model.Service.Lines.Count);
        Assert.Single(model.Service.Lines[1].Parts);
    }

    [Fact]
    public void TheLineAndPartCountsAreCapped()
    {
        var model = new DraftReviewFormModel();
        for (var count = 0; count < ServiceProposalForm.MaxLines; count++)
        {
            Assert.True(model.Service.AddLine());
        }

        Assert.False(model.Service.AddLine());

        for (var count = 0; count < ServiceProposalForm.MaxParts; count++)
        {
            Assert.True(model.Service.AddPart(0));
        }

        Assert.False(model.Service.AddPart(0));
    }

    [Theory]
    [InlineData(DraftKind.Fuel, FuelEnergyKind.Fuel)]
    [InlineData(DraftKind.Energy, FuelEnergyKind.Energy)]
    public void FuelAndChargeProposals_CarryTheirKind(DraftKind kind, FuelEnergyKind expected)
    {
        var model = DraftReviewFormModel.From(
            new DraftProposal { Kind = kind, FuelEnergy = new FuelEnergyProposal { Kind = expected, Date = new DateOnly(2026, 1, 2), UnitPrice = 3.5m, Quantity = 10m, TotalCost = 35m } },
            kind, null, false);

        var proposal = model.ToProposal();

        Assert.Equal(kind, proposal.Kind);
        Assert.Equal(expected, proposal.FuelEnergy!.Kind);
        Assert.Equal(35m, proposal.FuelEnergy.TotalCost);
        Assert.Null(proposal.ServiceRecord);
    }

    [Fact]
    public void TheOtherKinds_RoundTripOnlyTheirOwnSection()
    {
        var inspection = DraftReviewFormModel.From(new DraftProposal { Kind = DraftKind.Inspection, Inspection = new InspectionProposal { Type = InspectionType.Emissions, Date = new DateOnly(2026, 2, 3), Cost = 20m } }, DraftKind.Inspection, null, false).ToProposal();
        var registration = DraftReviewFormModel.From(new DraftProposal { Kind = DraftKind.Registration, Registration = new RegistrationProposal { State = "WA", Cost = 80m, ExpirationDate = new DateOnly(2027, 1, 1) } }, DraftKind.Registration, null, false).ToProposal();
        var insurance = DraftReviewFormModel.From(new DraftProposal { Kind = DraftKind.Insurance, Insurance = new InsuranceProposal { StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2027, 1, 1), Duration = InsuranceDuration.Annual, Cost = 900m } }, DraftKind.Insurance, null, false).ToProposal();
        var tax = DraftReviewFormModel.From(new DraftProposal { Kind = DraftKind.Tax, Tax = new TaxProposal { Type = TaxType.Lease, Amount = 50m, Recurrence = TaxRecurrence.Monthly } }, DraftKind.Tax, null, false).ToProposal();

        Assert.Equal(InspectionType.Emissions, inspection.Inspection!.Type);
        Assert.Null(inspection.Registration);
        Assert.Equal("WA", registration.Registration!.State);
        Assert.Equal(InsuranceDuration.Annual, insurance.Insurance!.Duration);
        Assert.Equal(TaxRecurrence.Monthly, tax.Tax!.Recurrence);
        Assert.Null(tax.Insurance);
    }

    [Fact]
    public void WithNoKindChosen_TheProposalIsEmpty()
    {
        var model = new DraftReviewFormModel { KindValue = string.Empty };

        var proposal = model.ToProposal();

        Assert.Null(proposal.Kind);
        Assert.Null(proposal.ServiceRecord);
    }

    [Fact]
    public void APostedFormThatLeftSectionsAndCollectionsNull_StillWorks()
    {
        var model = new DraftReviewFormModel { Service = null!, Fuel = null!, Tax = null! };
        model.Service.Lines = null!;
        model.Service.AddLine();
        model.Service.Lines[0].Parts = null!;

        Assert.Single(model.Service.Lines);
        Assert.Empty(model.Service.Lines[0].Parts);
        Assert.NotNull(model.Fuel);
        Assert.NotNull(model.Tax);
        Assert.Equal(0m, model.Service.Total);
    }

    [Fact]
    public void FromADraft_TakesTheProposedVehicleAndKind()
    {
        var vehicleId = Guid.NewGuid();
        var draft = new MaintenanceDraftDto
        {
            Id = Guid.NewGuid(),
            State = DraftState.NeedsReview,
            Kind = DraftKind.Tax,
            Proposal = new DraftProposal { Kind = DraftKind.Tax, Tax = new TaxProposal { Amount = 5m } },
            Vehicle = new DraftVehicleDto { VehicleId = vehicleId, Match = VehicleMatch.Vin, Candidates = [] },
            Source = new DraftSourceDto { Kind = "text" },
            Compatibility = new CompatibilityDto { IsAcceptable = false, Fields = [] },
            HasAttachment = false,
            ExpiresAt = DateTimeOffset.UtcNow,
        };

        var model = DraftReviewFormModel.From(draft);

        Assert.Equal(vehicleId, model.VehicleId);
        Assert.Equal("tax", model.KindValue);
        Assert.Equal(DraftKind.Tax, model.Kind);
        Assert.False(model.KeepOriginal);
        Assert.Equal(5m, model.Tax.Amount);
    }
}

public class DraftFormatTests
{
    [Theory]
    [InlineData(DraftKind.ServiceRecord, "service_record", "Service record")]
    [InlineData(DraftKind.Fuel, "fuel", "Fuel")]
    [InlineData(DraftKind.Energy, "energy", "Charge")]
    [InlineData(DraftKind.Inspection, "inspection", "Inspection")]
    [InlineData(DraftKind.Registration, "registration", "Registration")]
    [InlineData(DraftKind.Insurance, "insurance", "Insurance")]
    [InlineData(DraftKind.Tax, "tax", "Tax")]
    public void KindsHaveALabelAndAPostedValue_ThatParseBack(DraftKind kind, string value, string label)
    {
        Assert.Equal(value, DraftFormat.KindValue(kind));
        Assert.Equal(label, DraftFormat.KindLabel(kind));
        Assert.Equal(kind, DraftFormat.ParseKind(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("ServiceRecord")]
    public void AnUnknownKindText_MeansNoKind(string? value) => Assert.Null(DraftFormat.ParseKind(value));

    [Theory]
    [InlineData(DraftKind.ServiceRecord, MaintenanceSection.Records)]
    [InlineData(DraftKind.Fuel, MaintenanceSection.Fuel)]
    [InlineData(DraftKind.Energy, MaintenanceSection.Fuel)]
    [InlineData(DraftKind.Inspection, MaintenanceSection.Compliance)]
    [InlineData(DraftKind.Registration, MaintenanceSection.Compliance)]
    [InlineData(DraftKind.Insurance, MaintenanceSection.Compliance)]
    [InlineData(DraftKind.Tax, MaintenanceSection.Compliance)]
    public void EachKindReturnsToItsMaintenanceItem(DraftKind kind, MaintenanceSection section) =>
        Assert.Equal(section, DraftFormat.SectionFor(kind));

    [Theory]
    [InlineData("serviceRecord.dateServiced", "serviceRecord-dateServiced")]
    [InlineData("serviceRecord.lineItems[1].parts[0].unitPrice", "serviceRecord-lineItems-1-parts-0-unitPrice")]
    [InlineData("serviceRecord.lineItems[1]", "serviceRecord-lineItems-1")]
    [InlineData("fuelEnergy.date", "fuelEnergy-date")]
    [InlineData("kind", "kind")]
    public void CompatibilityPaths_BecomeElementIds(string path, string id) => Assert.Equal(id, DraftFormat.FieldId(path));

    [Theory]
    [InlineData("serviceRecord.odometerReading", "Odometer reading")]
    [InlineData("serviceRecord.lineItems[1].parts[0].unitPrice", "Line item 2, part 1: Unit price")]
    [InlineData("serviceRecord.lineItems[0].description", "Line item 1: Description")]
    [InlineData("serviceRecord.lineItems[1]", "Line item 2")]
    [InlineData("serviceRecord.lineItems", "Line items")]
    [InlineData("inspection.nextDueDate", "Next due date")]
    [InlineData("kind", "Record type")]
    public void CompatibilityPaths_BecomeReadableLabels(string path, string label) => Assert.Equal(label, DraftFormat.FieldLabel(path));
}
