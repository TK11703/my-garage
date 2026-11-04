using MyGarage.Api.Drafts;
using MyGarage.Api.FuelEnergyEntries;
using MyGarage.Api.Inspections;
using MyGarage.Api.Insurance;
using MyGarage.Api.Registrations;
using MyGarage.Api.ServiceRecords;
using MyGarage.Api.Taxes;
using MyGarage.Database.Drafts;
using MyGarage.Database.FuelEnergyEntries;
using MyGarage.Database.Inspections;
using MyGarage.Database.Insurance;
using MyGarage.Database.Registrations;
using MyGarage.Database.ServiceRecords;
using MyGarage.Database.Taxes;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Taxes;

namespace MyGarage.Api.Tests.Drafts;

public class DraftServiceTests
{
    private static readonly DateOnly Day = new(2026, 10, 20);

    private static DraftProposal FullService() =>
        new()
        {
            Kind = DraftKind.ServiceRecord,
            ServiceRecord = new ServiceRecordProposal
            {
                DateServiced = Day,
                OdometerReading = 45_500,
                ShopOrPerson = "  Joe's Garage ",
                DocumentTotal = 150m,
                LineItems =
                [
                    new ServiceLineItemRequest { Description = " Oil change ", FlatCost = 50m },
                    new ServiceLineItemRequest
                    {
                        Description = "Brakes",
                        LaborHours = 1m,
                        LaborRate = 60m,
                        Parts = [new ServicePartRequest { Name = " Pads ", Quantity = 2m, UnitPrice = 20m }],
                    },
                ],
            },
        };

    // ---- kind change carry-over ----

    [Fact]
    public void ChangingAServiceRecordToFuel_CarriesTheDateAndTheDocumentTotal_AndClearsTheRest()
    {
        var changed = DraftKindChanger.Change(FullService(), DraftKind.Fuel);

        Assert.Equal(DraftKind.Fuel, changed.Kind);
        Assert.Null(changed.ServiceRecord);
        var fuel = Assert.IsType<FuelEnergyProposal>(changed.FuelEnergy);
        Assert.Equal(FuelEnergyKind.Fuel, fuel.Kind);
        Assert.Equal(Day, fuel.Date);
        Assert.Equal(150m, fuel.TotalCost);
        Assert.Null(fuel.UnitPrice);
        Assert.Null(fuel.Quantity);
    }

    [Fact]
    public void ChangingToAnotherKind_UsesTheLineItemSum_WhenThereIsNoDocumentTotal()
    {
        var service = FullService();
        service = new DraftProposal
        {
            Kind = service.Kind,
            ServiceRecord = new ServiceRecordProposal
            {
                DateServiced = Day,
                OdometerReading = 1,
                LineItems = service.ServiceRecord!.LineItems,
            },
        };

        var changed = DraftKindChanger.Change(service, DraftKind.Inspection);

        Assert.Equal(DraftKind.Inspection, changed.Kind);
        Assert.Equal(Day, changed.Inspection!.Date);
        // 50.00 + 1 h x 60.00 + 2 x 20.00
        Assert.Equal(150m, changed.Inspection.Cost);
        Assert.Null(changed.Inspection.Type);
        Assert.Null(changed.Inspection.NextDueDate);
    }

    [Fact]
    public void ChangingAnInspectionToAServiceRecord_CarriesTheDateAndTheCost()
    {
        var inspection = new DraftProposal
        {
            Kind = DraftKind.Inspection,
            Inspection = new InspectionProposal { Type = InspectionType.Emissions, Date = Day, Cost = 25m, NextDueDate = Day.AddYears(1) },
        };

        var changed = DraftKindChanger.Change(inspection, DraftKind.ServiceRecord);

        Assert.Equal(DraftKind.ServiceRecord, changed.Kind);
        Assert.Null(changed.Inspection);
        Assert.Equal(Day, changed.ServiceRecord!.DateServiced);
        Assert.Equal(25m, changed.ServiceRecord.DocumentTotal);
        Assert.Null(changed.ServiceRecord.OdometerReading);
        Assert.Empty(changed.ServiceRecord.LineItems);
    }

    [Fact]
    public void ChangingFuelToEnergy_KeepsThePricingAndSwitchesTheEntryKind()
    {
        var fuel = new DraftProposal
        {
            Kind = DraftKind.Fuel,
            FuelEnergy = new FuelEnergyProposal { Kind = FuelEnergyKind.Fuel, Date = Day, UnitPrice = 3.4m, Quantity = 10m, TotalCost = 34m },
        };

        var changed = DraftKindChanger.Change(fuel, DraftKind.Energy);

        Assert.Equal(DraftKind.Energy, changed.Kind);
        Assert.Equal(FuelEnergyKind.Energy, changed.FuelEnergy!.Kind);
        Assert.Equal(3.4m, changed.FuelEnergy.UnitPrice);
        Assert.Equal(10m, changed.FuelEnergy.Quantity);
        Assert.Equal(34m, changed.FuelEnergy.TotalCost);
    }

    [Theory]
    [InlineData(DraftKind.Registration)]
    [InlineData(DraftKind.Insurance)]
    [InlineData(DraftKind.Tax)]
    public void ChangingToTheOtherKinds_CarriesTheTotalWhereTheFieldExists(DraftKind target)
    {
        var changed = DraftKindChanger.Change(FullService(), target);

        Assert.Equal(target, changed.Kind);
        Assert.Null(changed.ServiceRecord);
        var total = target switch
        {
            DraftKind.Registration => changed.Registration!.Cost,
            DraftKind.Insurance => changed.Insurance!.Cost,
            _ => changed.Tax!.Amount,
        };
        Assert.Equal(150m, total);
        DateOnly? expectedStart = target == DraftKind.Insurance ? Day : null;
        Assert.Equal(expectedStart, changed.Insurance?.StartDate);
    }

    [Fact]
    public void ChangingToTheSameKind_ReturnsTheProposalUnchanged()
    {
        var proposal = FullService();

        Assert.Same(proposal, DraftKindChanger.Change(proposal, DraftKind.ServiceRecord));
    }

    [Fact]
    public void ChangingAProposalWithNoKind_StartsAnEmptySectionOfTheTarget()
    {
        var changed = DraftKindChanger.Change(new DraftProposal(), DraftKind.Tax);

        Assert.Equal(DraftKind.Tax, changed.Kind);
        Assert.NotNull(changed.Tax);
        Assert.Null(changed.Tax.Amount);
    }

    // ---- accept request validation ----

    private static AcceptDraftRequestValidator NewValidator() => new(CompatibilityCheckerTests.NewChecker());

    [Fact]
    public async Task AnAcceptRequest_RequiresAVehicleAndAProposal()
    {
        var result = await NewValidator().ValidateAsync(new AcceptDraftRequest());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName.Equals("vehicleId", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.PropertyName.Equals("proposal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnAcceptRequest_ForAProposalWithoutAKind_IsRejected()
    {
        var result = await NewValidator().ValidateAsync(new AcceptDraftRequest { VehicleId = Guid.NewGuid(), Proposal = new DraftProposal() });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName.Equals("proposal.kind", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnAcceptRequest_WithAValidProposal_Passes()
    {
        var result = await NewValidator().ValidateAsync(new AcceptDraftRequest { VehicleId = Guid.NewGuid(), Proposal = FullService() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task AnAcceptRequest_ReportsEachInvalidFieldUnderItsPath()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.ServiceRecord,
            ServiceRecord = new ServiceRecordProposal
            {
                DateServiced = Day,
                LineItems = [new ServiceLineItemRequest { Description = "Oil", FlatCost = 10m, LaborHours = 1m, LaborRate = 10m }],
            },
        };

        var result = await NewValidator().ValidateAsync(new AcceptDraftRequest { VehicleId = Guid.NewGuid(), Proposal = proposal });

        Assert.False(result.IsValid);
        var paths = result.Errors.Select(error => error.PropertyName.ToLowerInvariant()).ToList();
        Assert.Contains("proposal.servicerecord.odometerreading", paths);
        Assert.Contains("proposal.servicerecord.lineitems[0]", paths);
    }

    [Fact]
    public async Task AnAcceptRequest_WithAnEmptyKindSection_IsRejectedForEveryRequiredField()
    {
        var result = await NewValidator().ValidateAsync(
            new AcceptDraftRequest { VehicleId = Guid.NewGuid(), Proposal = new DraftProposal { Kind = DraftKind.Tax } });

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
    }

    [Fact]
    public async Task AChangeKindRequest_RequiresAKnownKind()
    {
        var validator = new ChangeDraftKindRequestValidator();

        Assert.False((await validator.ValidateAsync(new ChangeDraftKindRequest())).IsValid);
        Assert.False((await validator.ValidateAsync(new ChangeDraftKindRequest { Kind = (DraftKind)99 })).IsValid);
        Assert.True((await validator.ValidateAsync(new ChangeDraftKindRequest { Kind = DraftKind.Insurance })).IsValid);
    }

    // ---- proposal -> accepted values, all seven kinds ----

    [Fact]
    public void AServiceRecordProposal_MapsToTrimmedValuesWithLineItemsAndParts()
    {
        var values = Assert.IsType<ServiceRecordValues>(DraftProposalMapper.ToValues(FullService()));

        Assert.Equal("service_record", values.DraftKind);
        Assert.Equal(Day, values.DateServiced);
        Assert.Equal(45_500, values.OdometerReading);
        Assert.Equal("Joe's Garage", values.ShopOrPerson);
        Assert.Equal(["Oil change", "Brakes"], values.LineItems.Select(line => line.Description));
        Assert.Equal(50m, values.LineItems[0].FlatCost);
        Assert.Equal(60m, values.LineItems[1].LaborRate);
        var part = Assert.Single(values.LineItems[1].Parts);
        Assert.Equal(("Pads", 2m, 20m), (part.Name, part.Quantity, part.UnitPrice));
    }

    [Fact]
    public void AServiceProposalWithADiscountALaborTotalAndATax_MapsToValuesWhoseTotalMatchesTheDocument()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.ServiceRecord,
            ServiceRecord = new ServiceRecordProposal
            {
                DateServiced = Day,
                OdometerReading = 45_500,
                LineItems =
                [
                    new ServiceLineItemRequest { Description = "Oil change", FlatCost = 50m },
                    new ServiceLineItemRequest { Description = "Coupon", FlatCost = -4.04m },
                    new ServiceLineItemRequest { Description = "Labor", LaborCost = 180m },
                ],
                Tax = 8.25m,
                DocumentTotal = 234.21m,
            },
        };

        var values = Assert.IsType<ServiceRecordValues>(DraftProposalMapper.ToValues(proposal));

        Assert.Equal(8.25m, values.TaxAmount);
        Assert.Equal(-4.04m, values.LineItems[1].TotalCost);
        Assert.Equal(180m, values.LineItems[2].StoredLaborCost);
        Assert.Equal(234.21m, values.LineItems.Sum(line => line.TotalCost) + values.TaxAmount);
        Assert.Equal(234.21m, DraftProposalMapper.RecordTotal(proposal.ServiceRecord));
        Assert.Equal(234.21m, DraftProposalMapper.HeadlineTotal(proposal));
        Assert.Equal((Day, (decimal?)234.21m), DraftProposalMapper.DateAndTotal(proposal));
    }

    [Fact]
    public void AZeroTaxOnAProposal_IsNoTaxOnTheValues()
    {
        var proposal = FullService();
        var withZero = new DraftProposal
        {
            Kind = DraftKind.ServiceRecord,
            ServiceRecord = new ServiceRecordProposal
            {
                DateServiced = proposal.ServiceRecord!.DateServiced,
                OdometerReading = proposal.ServiceRecord.OdometerReading,
                LineItems = proposal.ServiceRecord.LineItems,
                Tax = 0m,
            },
        };

        Assert.Null(Assert.IsType<ServiceRecordValues>(DraftProposalMapper.ToValues(withZero)).TaxAmount);
    }

    [Theory]
    [InlineData(DraftKind.Fuel, "fuel")]
    [InlineData(DraftKind.Energy, "energy")]
    public void AFuelOrEnergyProposal_MapsTheDraftKindAndTheCalculatedTotal(DraftKind kind, string stored)
    {
        var proposal = new DraftProposal
        {
            Kind = kind,
            FuelEnergy = new FuelEnergyProposal { Kind = FuelEnergyKind.Fuel, Date = Day, UnitPrice = 3.459m, Quantity = 12.345m },
        };

        var values = Assert.IsType<FuelEnergyValues>(DraftProposalMapper.ToValues(proposal));

        Assert.Equal(stored, values.Kind);
        Assert.Equal(stored, values.DraftKind);
        Assert.Equal(42.70m, values.TotalCost);
        Assert.Equal(Day, values.EntryDate);
    }

    [Fact]
    public void AnEnteredFuelTotal_IsKeptAsEntered()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.Fuel,
            FuelEnergy = new FuelEnergyProposal { Date = Day, UnitPrice = 3.459m, Quantity = 12.345m, TotalCost = 40m },
        };

        Assert.Equal(40m, Assert.IsType<FuelEnergyValues>(DraftProposalMapper.ToValues(proposal)).TotalCost);
    }

    [Fact]
    public void AnInspectionProposal_MapsToStoredValues()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.Inspection,
            Inspection = new InspectionProposal { Type = InspectionType.Emissions, Date = Day, Cost = 25m, NextDueDate = Day.AddYears(1) },
        };

        var values = Assert.IsType<InspectionValues>(DraftProposalMapper.ToValues(proposal));

        Assert.Equal(("emissions", Day, 25m, (DateOnly?)Day.AddYears(1)), (values.InspectionType, values.InspectionDate, values.Cost, values.NextDueDate));
    }

    [Fact]
    public void ARegistrationProposal_MapsToStoredValues()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.Registration,
            Registration = new RegistrationProposal { State = "WA", Cost = 78.75m, ExpirationDate = Day.AddYears(1) },
        };

        var values = Assert.IsType<RegistrationValues>(DraftProposalMapper.ToValues(proposal));

        Assert.Equal(("WA", 78.75m, Day.AddYears(1)), (values.State, values.Cost, values.ExpirationDate));
    }

    [Fact]
    public void AnInsuranceProposal_MapsToStoredValues()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.Insurance,
            Insurance = new InsuranceProposal { StartDate = Day, EndDate = Day.AddYears(1), Duration = InsuranceDuration.BiAnnual, Cost = 600m },
        };

        var values = Assert.IsType<InsuranceValues>(DraftProposalMapper.ToValues(proposal));

        Assert.Equal((Day, Day.AddYears(1), "bi_annual", 600m), (values.StartDate, values.EndDate, values.Duration, values.Cost));
    }

    [Fact]
    public void ATaxProposal_MapsToStoredValues()
    {
        var proposal = new DraftProposal
        {
            Kind = DraftKind.Tax,
            Tax = new TaxProposal { Type = TaxType.PersonalProperty, Amount = 312.40m, Recurrence = TaxRecurrence.Annual },
        };

        var values = Assert.IsType<TaxValues>(DraftProposalMapper.ToValues(proposal));

        Assert.Equal(("personal_property", 312.40m, "annual"), (values.TaxType, values.Amount, values.Recurrence));
    }

    [Fact]
    public void TheHeadlineTotal_IsTheDocumentTotalTheLineSumTheCostOrTheAmount()
    {
        Assert.Equal(150m, DraftProposalMapper.HeadlineTotal(FullService()));
        var lines = FullService().ServiceRecord!.LineItems;
        Assert.Equal(
            150m,
            DraftProposalMapper.HeadlineTotal(new DraftProposal { Kind = DraftKind.ServiceRecord, ServiceRecord = new ServiceRecordProposal { LineItems = lines } }));
        Assert.Null(DraftProposalMapper.HeadlineTotal(new DraftProposal { Kind = DraftKind.ServiceRecord, ServiceRecord = new ServiceRecordProposal() }));
        Assert.Equal(
            42.70m,
            DraftProposalMapper.HeadlineTotal(new DraftProposal { Kind = DraftKind.Fuel, FuelEnergy = new FuelEnergyProposal { UnitPrice = 3.459m, Quantity = 12.345m } }));
        Assert.Equal(25m, DraftProposalMapper.HeadlineTotal(new DraftProposal { Kind = DraftKind.Inspection, Inspection = new InspectionProposal { Cost = 25m } }));
        Assert.Equal(12m, DraftProposalMapper.HeadlineTotal(new DraftProposal { Kind = DraftKind.Tax, Tax = new TaxProposal { Amount = 12m } }));
        Assert.Null(DraftProposalMapper.HeadlineTotal(new DraftProposal()));
    }

    // ---- from_email in the DTO mappers of the six existing features ----

    private static readonly DateTimeOffset At = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheServiceRecordMapper_CopiesFromEmailIntoTheSummaryAndTheDetail(bool fromEmail)
    {
        var record = new ServiceRecordRecord
        {
            Id = Guid.NewGuid(), VehicleId = Guid.NewGuid(), DateServiced = Day, OdometerReading = 1, CreatedAt = At, UpdatedAt = At, FromEmail = fromEmail,
        };

        Assert.Equal(fromEmail, ServiceRecordMapper.ToSummaryDto(record).FromEmail);
        Assert.Equal(fromEmail, ServiceRecordMapper.ToDto(new ServiceRecordDetailRecord(record, [])).FromEmail);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheMaintenanceMappers_CopyFromEmail(bool fromEmail)
    {
        Assert.Equal(fromEmail, FuelEnergyEntryMapper.ToDto(new FuelEnergyEntryRecord { Kind = "fuel", CreatedAt = At, UpdatedAt = At, FromEmail = fromEmail }).FromEmail);
        Assert.Equal(fromEmail, InspectionMapper.ToDto(new InspectionRecord { InspectionType = "safety", CreatedAt = At, UpdatedAt = At, FromEmail = fromEmail }).FromEmail);
        Assert.Equal(fromEmail, RegistrationMapper.ToDto(new RegistrationRecord { State = "WA", CreatedAt = At, UpdatedAt = At, FromEmail = fromEmail }).FromEmail);
        Assert.Equal(fromEmail, InsurancePolicyMapper.ToDto(new InsurancePolicyRecord { Duration = "annual", CreatedAt = At, UpdatedAt = At, FromEmail = fromEmail }).FromEmail);
        Assert.Equal(fromEmail, TaxMapper.ToDto(new TaxRecord { TaxType = "lease", Recurrence = "monthly", CreatedAt = At, UpdatedAt = At, FromEmail = fromEmail }).FromEmail);
    }

    [Fact]
    public void TheFromEmailFlag_DefaultsToFalseOnEveryDto()
    {
        Assert.False(new ServiceRecordSummaryDto { Id = Guid.NewGuid(), DateServiced = Day, OdometerReading = 1, LineItemCount = 0, TotalCost = 0 }.FromEmail);
        Assert.False(new TaxDto { Id = Guid.NewGuid(), VehicleId = Guid.NewGuid(), Type = TaxType.Lease, Amount = 1, Recurrence = TaxRecurrence.Once, CreatedAt = At, UpdatedAt = At }.FromEmail);
    }
}
