using MyGarage.Api.Drafts;
using MyGarage.Api.FuelEnergyEntries;
using MyGarage.Api.Inspections;
using MyGarage.Api.Insurance;
using MyGarage.Api.Registrations;
using MyGarage.Api.ServiceRecords;
using MyGarage.Api.Taxes;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Taxes;

namespace MyGarage.Api.Tests.Drafts;

public class CompatibilityCheckerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeDuplicates : IDraftDuplicateLookup
    {
        public bool SameAttachment { get; set; }

        public bool SameRecord { get; set; }

        public List<string> RecordLookups { get; } = [];

        public Task<bool> HasDraftWithSameAttachmentAsync(string ownerId, Guid draftId, string sha256, CancellationToken cancellationToken) =>
            Task.FromResult(SameAttachment);

        public Task<bool> HasRecordLikeAsync(string ownerId, Guid vehicleId, string kind, DateOnly date, decimal total, CancellationToken cancellationToken)
        {
            RecordLookups.Add($"{kind}|{date:yyyy-MM-dd}|{total}");
            return Task.FromResult(SameRecord);
        }
    }

    internal static CompatibilityChecker NewChecker(IDraftDuplicateLookup? duplicates = null)
    {
        var clock = new TestTimeProvider(Now);
        return new CompatibilityChecker(
            new ServiceRecordRequestValidator(clock),
            new ServiceLineItemRequestValidator(),
            new FuelEnergyEntryRequestValidator(clock),
            new InspectionRequestValidator(clock),
            new RegistrationRequestValidator(),
            new InsurancePolicyRequestValidator(),
            new TaxRequestValidator(),
            duplicates ?? new FakeDuplicates());
    }

    private static readonly DraftCheckContext Context = new("owner-a");

    private static ServiceLineItemRequest Flat(string description = "Oil change", decimal flat = 59.99m) =>
        new() { Description = description, FlatCost = flat };

    private static ServiceRecordProposal ServiceRecord(params ServiceLineItemRequest[] lines) =>
        new() { DateServiced = new DateOnly(2026, 10, 20), OdometerReading = 45_500, ShopOrPerson = "Joe's Garage", LineItems = lines };

    private static DraftProposal Service(ServiceRecordProposal section) => new() { Kind = DraftKind.ServiceRecord, ServiceRecord = section };

    private static CompatibilityFieldDto Field(CompatibilityDto result, string path) => Assert.Single(result.Fields, field => field.Path == path);

    [Fact]
    public async Task ACompleteServiceRecord_IsAcceptable_WithEveryFieldOkAndNoWarnings()
    {
        var proposal = Service(ServiceRecord(
            Flat(),
            new ServiceLineItemRequest
            {
                Description = "Brakes",
                LaborHours = 1.5m,
                LaborRate = 120m,
                Parts = [new ServicePartRequest { Name = "Pads", Quantity = 2m, UnitPrice = 35.25m }],
            }));

        var result = await NewChecker().CheckAsync(proposal, Context, CancellationToken.None);

        Assert.True(result.IsAcceptable);
        Assert.Equal(DraftKind.ServiceRecord, result.Kind);
        Assert.All(result.Fields, field => Assert.Equal(CompatibilityStatus.Ok, field.Status));
        Assert.Contains(result.Fields, field => field.Path == "serviceRecord.dateServiced");
        Assert.Contains(result.Fields, field => field.Path == "serviceRecord.odometerReading");
        Assert.Contains(result.Fields, field => field.Path == "serviceRecord.lineItems[0].description");
        Assert.Contains(result.Fields, field => field.Path == "serviceRecord.lineItems[1].parts[0].unitPrice");
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task AMissingOdometer_IsMissing_AndTheDraftIsNotAcceptable()
    {
        var section = new ServiceRecordProposal { DateServiced = new DateOnly(2026, 10, 20), LineItems = [Flat()] };

        var result = await NewChecker().CheckAsync(Service(section), Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        Assert.Equal(CompatibilityStatus.Missing, Field(result, "serviceRecord.odometerReading").Status);
        Assert.Equal(CompatibilityStatus.Ok, Field(result, "serviceRecord.dateServiced").Status);
    }

    [Fact]
    public async Task AFutureServiceDate_IsInvalid_WithTheExistingValidatorsMessage()
    {
        var section = new ServiceRecordProposal { DateServiced = new DateOnly(2026, 12, 25), OdometerReading = 1000, LineItems = [Flat()] };

        var result = await NewChecker().CheckAsync(Service(section), Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        var field = Field(result, "serviceRecord.dateServiced");
        Assert.Equal(CompatibilityStatus.Invalid, field.Status);
        Assert.Equal("Date serviced cannot be in the future.", field.Message);
    }

    [Fact]
    public async Task ALineWithBothAFlatCostAndLabor_IsInvalidAtTheLineItem()
    {
        var both = new ServiceLineItemRequest { Description = "Brakes", FlatCost = 100m, LaborHours = 1m, LaborRate = 90m };

        var result = await NewChecker().CheckAsync(Service(ServiceRecord(Flat(), both)), Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        var field = Field(result, "serviceRecord.lineItems[1]");
        Assert.Equal(CompatibilityStatus.Invalid, field.Status);
        Assert.Equal("Use either a flat cost or labor and parts, not both.", field.Message);
    }

    [Fact]
    public async Task ANegativePartPrice_IsInvalidAtThePartsUnitPrice()
    {
        var line = new ServiceLineItemRequest
        {
            Description = "Brakes",
            Parts = [new ServicePartRequest { Name = "Pads", Quantity = 1m, UnitPrice = -5m }],
        };

        var result = await NewChecker().CheckAsync(Service(ServiceRecord(line)), Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        Assert.Equal(CompatibilityStatus.Invalid, Field(result, "serviceRecord.lineItems[0].parts[0].unitPrice").Status);
        Assert.Equal(CompatibilityStatus.Ok, Field(result, "serviceRecord.lineItems[0].parts[0].name").Status);
    }

    [Fact]
    public async Task AMissingPartPriceOnTheSecondLine_IsMissingAtThePathOfTheContract()
    {
        var second = new ServiceLineItemRequest
        {
            Description = "Brakes",
            Parts = [new ServicePartRequest { Name = "Pads", Quantity = 1m }],
        };

        var result = await NewChecker().CheckAsync(Service(ServiceRecord(Flat(), second)), Context, CancellationToken.None);

        Assert.Equal(CompatibilityStatus.Missing, Field(result, "serviceRecord.lineItems[1].parts[0].unitPrice").Status);
        Assert.False(result.IsAcceptable);
    }

    [Fact]
    public async Task AMissingDescription_IsMissing()
    {
        var result = await NewChecker().CheckAsync(
            Service(ServiceRecord(new ServiceLineItemRequest { FlatCost = 10m })), Context, CancellationToken.None);

        Assert.Equal(CompatibilityStatus.Missing, Field(result, "serviceRecord.lineItems[0].description").Status);
    }

    [Fact]
    public async Task ARecordWithNoLineItems_IsAcceptable_WithAWarning()
    {
        var result = await NewChecker().CheckAsync(Service(ServiceRecord()), Context, CancellationToken.None);

        Assert.True(result.IsAcceptable);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("No line items were found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MoreThanFiftyLineItems_AreInvalid()
    {
        var lines = Enumerable.Range(0, 51).Select(index => Flat($"Item {index}", 1m)).ToArray();

        var result = await NewChecker().CheckAsync(Service(ServiceRecord(lines)), Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        Assert.Equal(CompatibilityStatus.Invalid, Field(result, "serviceRecord.lineItems").Status);
    }

    [Fact]
    public async Task LinesThatAddUpDifferentlyFromTheDocumentTotal_WarnButStayAcceptable()
    {
        var section = new ServiceRecordProposal
        {
            DateServiced = new DateOnly(2026, 10, 20),
            OdometerReading = 1000,
            LineItems = [Flat(flat: 100m)],
            DocumentTotal = 125m,
        };

        var result = await NewChecker().CheckAsync(Service(section), Context, CancellationToken.None);

        Assert.True(result.IsAcceptable);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("100.00", warning);
        Assert.Contains("125.00", warning);
    }

    [Fact]
    public async Task LinesThatMatchTheDocumentTotal_GiveNoWarning()
    {
        var section = new ServiceRecordProposal
        {
            DateServiced = new DateOnly(2026, 10, 20),
            OdometerReading = 1000,
            LineItems = [Flat(flat: 100m)],
            DocumentTotal = 100m,
        };

        var result = await NewChecker().CheckAsync(Service(section), Context, CancellationToken.None);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task AProbableDuplicate_ByAttachmentHash_WarnsOnlyWhenTheContextHasAHash()
    {
        var duplicates = new FakeDuplicates { SameAttachment = true };
        var proposal = Service(ServiceRecord(Flat()));
        var checker = NewChecker(duplicates);

        var withHash = await checker.CheckAsync(
            proposal, new DraftCheckContext("owner-a", Guid.NewGuid(), null, new string('a', 64)), CancellationToken.None);
        var withoutHash = await checker.CheckAsync(proposal, new DraftCheckContext("owner-a", Guid.NewGuid()), CancellationToken.None);

        Assert.Contains(withHash.Warnings, warning => warning.Contains("same file", StringComparison.OrdinalIgnoreCase));
        Assert.True(withHash.IsAcceptable);
        Assert.Empty(withoutHash.Warnings);
    }

    [Fact]
    public async Task AProbableDuplicate_ByDateAndTotalOnTheVehicle_Warns()
    {
        var duplicates = new FakeDuplicates { SameRecord = true };
        var proposal = Service(ServiceRecord(Flat(flat: 80m)));

        var result = await NewChecker(duplicates).CheckAsync(
            proposal, new DraftCheckContext("owner-a", Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(["service_record|2026-10-20|80"], duplicates.RecordLookups);
        Assert.Contains(result.Warnings, warning => warning.Contains("same date and total", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.IsAcceptable);
    }

    [Fact]
    public async Task ThereIsNoRecordLookup_WithoutAVehicleOrWithoutADateAndTotal()
    {
        var duplicates = new FakeDuplicates { SameRecord = true };
        var checker = NewChecker(duplicates);

        await checker.CheckAsync(Service(ServiceRecord(Flat())), Context, CancellationToken.None);
        await checker.CheckAsync(
            Service(new ServiceRecordProposal { OdometerReading = 1 }), new DraftCheckContext("owner-a", null, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(duplicates.RecordLookups);
    }

    [Fact]
    public async Task ANullKind_IsNotAcceptable_WithAMissingKind()
    {
        var result = await NewChecker().CheckAsync(new DraftProposal(), Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        Assert.Null(result.Kind);
        Assert.Equal(CompatibilityStatus.Missing, Field(result, "kind").Status);
    }

    [Fact]
    public async Task AKindWithAnEmptySection_HasEveryRequiredFieldMissing()
    {
        var result = await NewChecker().CheckAsync(new DraftProposal { Kind = DraftKind.Registration }, Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        Assert.Equal(CompatibilityStatus.Missing, Field(result, "registration.state").Status);
        Assert.Equal(CompatibilityStatus.Missing, Field(result, "registration.cost").Status);
        Assert.Equal(CompatibilityStatus.Missing, Field(result, "registration.expirationDate").Status);
    }

    [Fact]
    public async Task AFuelProposal_RunsTheFuelValidator_AndLabelsTheDateField()
    {
        var good = new DraftProposal
        {
            Kind = DraftKind.Fuel,
            FuelEnergy = new FuelEnergyProposal { Date = new DateOnly(2026, 10, 1), UnitPrice = 3.459m, Quantity = 12.5m },
        };
        var noDate = new DraftProposal { Kind = DraftKind.Energy, FuelEnergy = new FuelEnergyProposal { UnitPrice = 0.13m, Quantity = 20m } };

        var checker = NewChecker();
        var okResult = await checker.CheckAsync(good, Context, CancellationToken.None);
        var failing = await checker.CheckAsync(noDate, Context, CancellationToken.None);

        Assert.True(okResult.IsAcceptable);
        Assert.False(failing.IsAcceptable);
        Assert.Equal(CompatibilityStatus.Missing, Field(failing, "fuelEnergy.date").Status);
        Assert.Equal(CompatibilityStatus.Ok, Field(failing, "fuelEnergy.unitPrice").Status);
    }

    [Fact]
    public async Task AnInspectionWithoutACost_IsMissing()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.Inspection,
            Inspection = new InspectionProposal { Type = InspectionType.Safety, Date = new DateOnly(2026, 10, 1) },
        };

        var result = await NewChecker().CheckAsync(proposal, Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        Assert.Equal(CompatibilityStatus.Missing, Field(result, "inspection.cost").Status);
        Assert.Equal(CompatibilityStatus.Ok, Field(result, "inspection.type").Status);
    }

    [Fact]
    public async Task ARegistrationWithAnUnknownState_IsInvalid()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.Registration,
            Registration = new MyGarage.Shared.Drafts.RegistrationProposal { State = "ZZ", Cost = 78.75m, ExpirationDate = new DateOnly(2027, 5, 1) },
        };

        var result = await NewChecker().CheckAsync(proposal, Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        Assert.Equal(CompatibilityStatus.Invalid, Field(result, "registration.state").Status);
    }

    [Fact]
    public async Task AnInsurancePolicyEndingBeforeItStarts_IsInvalid()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.Insurance,
            Insurance = new InsuranceProposal
            {
                StartDate = new DateOnly(2026, 6, 1),
                EndDate = new DateOnly(2026, 1, 1),
                Duration = InsuranceDuration.Annual,
                Cost = 1240m,
            },
        };

        var result = await NewChecker().CheckAsync(proposal, Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        Assert.Equal(CompatibilityStatus.Invalid, Field(result, "insurance.endDate").Status);
    }

    [Fact]
    public async Task ATaxWithAZeroAmount_IsInvalid()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.Tax,
            Tax = new TaxProposal { Type = TaxType.Lease, Amount = 0m, Recurrence = TaxRecurrence.Monthly },
        };

        var result = await NewChecker().CheckAsync(proposal, Context, CancellationToken.None);

        Assert.False(result.IsAcceptable);
        Assert.Equal(CompatibilityStatus.Invalid, Field(result, "tax.amount").Status);
    }

    [Fact]
    public void Validate_ReturnsTheFailuresWithMissingFlagged()
    {
        var failures = NewChecker().Validate(new DraftProposal { Kind = DraftKind.Tax, Tax = new TaxProposal { Amount = 0m } });

        Assert.Contains(failures, failure => failure.Path == "tax.type" && failure.IsMissing);
        Assert.Contains(failures, failure => failure.Path == "tax.amount" && !failure.IsMissing);
        Assert.Empty(NewChecker().Validate(Service(ServiceRecord(Flat()))));
    }
}
