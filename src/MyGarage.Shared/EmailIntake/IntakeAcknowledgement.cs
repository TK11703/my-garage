namespace MyGarage.Shared.EmailIntake;

// The same answer for a matched sender, an unknown sender, and a repeated message id.
public sealed class IntakeAcknowledgement
{
    public const string Received = "received";

    public required Guid SubmissionId { get; init; }

    public string Status { get; init; } = Received;
}
