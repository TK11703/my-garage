namespace MyGarage.Database.EmailIntake;

public sealed class IntakeStorageUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
