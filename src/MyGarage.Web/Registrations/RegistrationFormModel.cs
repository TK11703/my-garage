using MyGarage.Shared.Registrations;

namespace MyGarage.Web.Registrations;

public sealed class RegistrationFormModel
{
    public string? State { get; set; }

    public decimal? Cost { get; set; }

    public DateOnly? ExpirationDate { get; set; }

    public RegistrationRequest ToRequest() =>
        new() { State = State, Cost = Cost, ExpirationDate = ExpirationDate };

    public static RegistrationFormModel From(RegistrationDto registration) =>
        new()
        {
            State = registration.State,
            Cost = registration.Cost,
            ExpirationDate = registration.ExpirationDate,
        };
}
