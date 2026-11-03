namespace MyGarage.Database.VehicleDocuments;

// Raised when the document store cannot be reached or rejects a request; document data is never partially stored.
public sealed class DocumentStorageUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
