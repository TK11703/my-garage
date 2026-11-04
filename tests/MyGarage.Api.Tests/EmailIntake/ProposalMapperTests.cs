using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.Taxes;

namespace MyGarage.Api.Tests.EmailIntake;

public class ProposalMapperTests
{
    private static DraftProposal Map(ExtractionResult result) => Assert.IsType<DraftProposal>(ProposalMapper.Map(result));

    private static ExtractionResult ServiceResult(params ExtractedLineItem[] lines) =>
        new() { Kind = ExtractionKind.ServiceRecord, Date = "2026-10-12", OdometerReading = 48210, ShopOrPerson = "Main St Garage", LineItems = lines };

    [Fact]
    public void ServiceRecord_MapsTheHeader_AndParsesTheDate()
    {
        var proposal = Map(ServiceResult());

        Assert.Equal(DraftKind.ServiceRecord, proposal.Kind);
        var section = Assert.IsType<ServiceRecordProposal>(proposal.ServiceRecord);
        Assert.Equal(new DateOnly(2026, 10, 12), section.DateServiced);
        Assert.Equal(48210, section.OdometerReading);
        Assert.Equal("Main St Garage", section.ShopOrPerson);
        Assert.Empty(section.LineItems);
        Assert.Null(proposal.FuelEnergy);
        Assert.Null(proposal.Tax);
    }

    [Fact]
    public void ServiceRecord_TakesTheOdometerFromTheVehicleHint_WhenTheFieldIsMissing()
    {
        var result = ServiceResult() with { OdometerReading = null, VehicleHint = new VehicleHint(null, null, null, null, 51000) };

        Assert.Equal(51000, Map(result).ServiceRecord!.OdometerReading);
    }

    [Fact]
    public void ALineWithLaborHoursAndRate_BecomesALaborLine_AndClearsTheFlatCost()
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Brake job", 120m, 1.5m, 80m, null))).ServiceRecord!.LineItems.Single();

        Assert.Equal("Brake job", line.Description);
        Assert.Null(line.FlatCost);
        Assert.Equal(1.5m, line.LaborHours);
        Assert.Equal(80m, line.LaborRate);
    }

    [Fact]
    public void ALineWithParts_KeepsThem_AndClearsTheFlatCost()
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Oil change", 59.99m, null, null, [new ExtractedPart("Oil filter", 1m, 8.5m), new ExtractedPart("Oil 5W-30", 5m, 7.25m)])))
            .ServiceRecord!.LineItems.Single();

        Assert.Null(line.FlatCost);
        Assert.Equal(2, line.Parts.Count);
        Assert.Equal("Oil filter", line.Parts[0].Name);
        Assert.Equal(5m, line.Parts[1].Quantity);
        Assert.Equal(7.25m, line.Parts[1].UnitPrice);
    }

    [Fact]
    public void ALineWithOnlyATotal_BecomesAFlatCostLine()
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Tire rotation", 25m, null, null, null))).ServiceRecord!.LineItems.Single();

        Assert.Equal(25m, line.FlatCost);
        Assert.Null(line.LaborHours);
        Assert.Null(line.LaborRate);
        Assert.Empty(line.Parts);
    }

    [Fact]
    public void BothFlatCostAndLabor_KeepsTheLabor()
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Alignment", 99m, 1m, 80m, []))).ServiceRecord!.LineItems.Single();

        Assert.Null(line.FlatCost);
        Assert.Equal(1m, line.LaborHours);
        Assert.Equal(80m, line.LaborRate);
    }

    [Fact]
    public void ADiscountLine_KeepsItsNegativeFlatCost()
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Coupon", -4.04m, null, null, null))).ServiceRecord!.LineItems.Single();

        Assert.Equal(-4.04m, line.FlatCost);
        Assert.Null(line.LaborCost);
    }

    [Fact]
    public void ALaborTotalWithoutHoursOrRate_IsKeptAsTheLaborCost_AndClearsTheFlatCost()
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Labor", 180m, null, null, null, 180m))).ServiceRecord!.LineItems.Single();

        Assert.Null(line.FlatCost);
        Assert.Equal(180m, line.LaborCost);
        Assert.Null(line.LaborHours);
        Assert.Null(line.LaborRate);
    }

    [Fact]
    public void ALaborTotalWithParts_KeepsBoth()
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Brake job", null, null, null, [new ExtractedPart("Pads", 1m, 25m)], 180m))).ServiceRecord!.LineItems.Single();

        Assert.Equal(180m, line.LaborCost);
        Assert.Single(line.Parts);
    }

    [Theory]
    [InlineData(2.5, null)]
    [InlineData(null, 120.0)]
    public void ALoneHoursOrRate_IsDropped_AndTheLaborTotalIsKept(double? hours, double? rate)
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Labor", null, (decimal?)hours, (decimal?)rate, null, 180m))).ServiceRecord!.LineItems.Single();

        Assert.Null(line.LaborHours);
        Assert.Null(line.LaborRate);
        Assert.Equal(180m, line.LaborCost);
    }

    [Fact]
    public void ALoneHoursWithAFlatCostAndNoLaborTotal_FallsBackToTheFlatCost()
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Alignment", 99m, 1m, null, []))).ServiceRecord!.LineItems.Single();

        Assert.Equal(99m, line.FlatCost);
        Assert.Null(line.LaborHours);
    }

    [Fact]
    public void HoursAndRateTogetherWithALaborTotal_KeepTheCalculationAndDropTheTotal()
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Labor", null, 2.5m, 120m, null, 999m))).ServiceRecord!.LineItems.Single();

        Assert.Equal(2.5m, line.LaborHours);
        Assert.Equal(120m, line.LaborRate);
        Assert.Null(line.LaborCost);
    }

    [Fact]
    public void TheTax_IsRoundedAndKeptOutOfTheLineItems_AndZeroMeansNone()
    {
        var withTax = ServiceResult(new ExtractedLineItem("Oil change", 50m, null, null, null)) with { Tax = 8.255m };
        var zero = ServiceResult(new ExtractedLineItem("Oil change", 50m, null, null, null)) with { Tax = 0m };

        var section = Map(withTax).ServiceRecord!;

        Assert.Equal(8.26m, section.Tax);
        Assert.Single(section.LineItems);
        Assert.Null(Map(zero).ServiceRecord!.Tax);
        Assert.Null(Map(ServiceResult()).ServiceRecord!.Tax);
    }

    [Fact]
    public void Text_IsTrimmedAndCutToTheColumnLengths()
    {
        var result = ServiceResult(new ExtractedLineItem(
            "  " + new string('d', 250) + "  ", null, null, null, [new ExtractedPart(new string('p', 150), 1m, 1m)])) with { ShopOrPerson = " " + new string('s', 130) };

        var section = Map(result).ServiceRecord!;

        Assert.Equal(new string('s', 100), section.ShopOrPerson);
        Assert.Equal(new string('d', 200), section.LineItems[0].Description);
        Assert.Equal(new string('p', 100), section.LineItems[0].Parts[0].Name);
    }

    [Fact]
    public void Money_IsRoundedToTwoDecimals()
    {
        var line = Map(ServiceResult(new ExtractedLineItem("Pads", 19.999m, null, null, null))).ServiceRecord!.LineItems.Single();
        var part = Map(ServiceResult(new ExtractedLineItem("Pads", null, null, null, [new ExtractedPart("Pad", 2m, 10.456m)]))).ServiceRecord!.LineItems.Single().Parts.Single();

        Assert.Equal(20.00m, line.FlatCost);
        Assert.Equal(10.46m, part.UnitPrice);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("2026-13-45")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnparseableDate_BecomesNull(string? date)
    {
        Assert.Null(Map(ServiceResult() with { Date = date }).ServiceRecord!.DateServiced);
    }

    [Fact]
    public void Fuel_AndEnergy_MapToTheFuelEnergyShape()
    {
        var fuel = Map(new ExtractionResult { Kind = ExtractionKind.Fuel, Date = "2026-10-01", UnitPrice = 3.499m, Quantity = 12.5m, TotalCost = 43.74m });
        var energy = Map(new ExtractionResult { Kind = ExtractionKind.Energy, Date = "2026-10-02", UnitPrice = 0.31m, Quantity = 40m });

        Assert.Equal(DraftKind.Fuel, fuel.Kind);
        Assert.Equal(FuelEnergyKind.Fuel, fuel.FuelEnergy!.Kind);
        Assert.Equal(new DateOnly(2026, 10, 1), fuel.FuelEnergy.Date);
        Assert.Equal(3.499m, fuel.FuelEnergy.UnitPrice);
        Assert.Equal(12.5m, fuel.FuelEnergy.Quantity);
        Assert.Equal(43.74m, fuel.FuelEnergy.TotalCost);
        Assert.Equal(DraftKind.Energy, energy.Kind);
        Assert.Equal(FuelEnergyKind.Energy, energy.FuelEnergy!.Kind);
        Assert.Null(energy.FuelEnergy.TotalCost);
    }

    [Fact]
    public void Inspection_Registration_Insurance_AndTax_MapToTheirShapes()
    {
        var inspection = Map(new ExtractionResult { Kind = ExtractionKind.Inspection, Type = "Emissions", Date = "2026-09-30", TotalCost = 20m, NextDueDate = "2027-09-30" });
        var registration = Map(new ExtractionResult { Kind = ExtractionKind.Registration, State = " wa ", TotalCost = 75m, ExpirationDate = "2027-03-31" });
        var insurance = Map(new ExtractionResult { Kind = ExtractionKind.Insurance, StartDate = "2026-07-01", EndDate = "2027-01-01", Duration = "bi_annual", TotalCost = 640m });
        var tax = Map(new ExtractionResult { Kind = ExtractionKind.Tax, Type = "personal_property", TotalCost = 310m, Recurrence = "annual" });

        Assert.Equal(InspectionType.Emissions, inspection.Inspection!.Type);
        Assert.Equal(new DateOnly(2026, 9, 30), inspection.Inspection.Date);
        Assert.Equal(20m, inspection.Inspection.Cost);
        Assert.Equal(new DateOnly(2027, 9, 30), inspection.Inspection.NextDueDate);
        Assert.Equal("WA", registration.Registration!.State);
        Assert.Equal(75m, registration.Registration.Cost);
        Assert.Equal(new DateOnly(2027, 3, 31), registration.Registration.ExpirationDate);
        Assert.Equal(InsuranceDuration.BiAnnual, insurance.Insurance!.Duration);
        Assert.Equal(new DateOnly(2026, 7, 1), insurance.Insurance.StartDate);
        Assert.Equal(new DateOnly(2027, 1, 1), insurance.Insurance.EndDate);
        Assert.Equal(640m, insurance.Insurance.Cost);
        Assert.Equal(TaxType.PersonalProperty, tax.Tax!.Type);
        Assert.Equal(310m, tax.Tax.Amount);
        Assert.Equal(TaxRecurrence.Annual, tax.Tax.Recurrence);
    }

    [Fact]
    public void UnknownEnumTextAndABadState_BecomeNull()
    {
        var inspection = Map(new ExtractionResult { Kind = ExtractionKind.Inspection, Type = "annual checkup" });
        var registration = Map(new ExtractionResult { Kind = ExtractionKind.Registration, State = "Washington" });
        var insurance = Map(new ExtractionResult { Kind = ExtractionKind.Insurance, Duration = "weekly" });
        var tax = Map(new ExtractionResult { Kind = ExtractionKind.Tax, Type = "sales", Recurrence = "sometimes" });

        Assert.Null(inspection.Inspection!.Type);
        Assert.Null(registration.Registration!.State);
        Assert.Null(insurance.Insurance!.Duration);
        Assert.Null(tax.Tax!.Type);
        Assert.Null(tax.Tax.Recurrence);
    }

    [Fact]
    public void KindNone_AndAnUnknownKind_MapToNoProposal()
    {
        Assert.Null(ProposalMapper.Map(new ExtractionResult { Kind = ExtractionKind.None }));
        Assert.Null(ProposalMapper.Map(new ExtractionResult { Kind = (ExtractionKind)99 }));
    }

    [Fact]
    public void Text_IsPlainText_NotInterpreted()
    {
        const string payload = "<script>alert(1)</script> **bold** {{x}}";

        var section = Map(ServiceResult() with { ShopOrPerson = payload }).ServiceRecord!;

        Assert.Equal(payload, section.ShopOrPerson);
    }

    [Fact]
    public void Notes_AreTrimmedAndLimitedTo500Characters()
    {
        Assert.Equal(new string('n', 500), ProposalMapper.NotesFor(new ExtractionResult { Notes = "  " + new string('n', 600) }));
        Assert.Null(ProposalMapper.NotesFor(new ExtractionResult { Notes = "   " }));
        Assert.Null(ProposalMapper.NotesFor(new ExtractionResult()));
    }
}
