namespace PredictionLeague.Application.Abstractions;

// Thrown by a CSV importer when the uploaded file can't be parsed (malformed rows, bad encoding).
// Controllers map it to a 400 ProblemDetails so a bad upload is a caller error, not an opaque 500.
public class CsvImportException : Exception
{
    public CsvImportException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
