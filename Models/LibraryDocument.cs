namespace OnLibrary.Models;

public record LibraryDocument(int Id, string Title, string Author, string Category, int Year,
    string Format, string Color, string Initials, string Description, double Rating, int Reads)
{
    public string? StoredFileName { get; init; }
    public string? FileMimeType { get; init; }
    public long? FileSizeBytes { get; init; }
}
