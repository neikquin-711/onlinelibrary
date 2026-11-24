namespace OnLibrary.Models;

public sealed class LibraryDocument
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Category { get; set; } = "";
    public int Year { get; set; }
    public string Format { get; set; } = "PDF";
    public string Color { get; set; } = "sand";
    public string Initials { get; set; } = "";
    public string Description { get; set; } = "";
    public double Rating { get; set; }
    public int Reads { get; set; }
    public string? StoredFileName { get; set; }
    public string? FileMimeType { get; set; }
    public long? FileSizeBytes { get; set; }

    public LibraryDocument() { }

    public LibraryDocument(int id, string title, string author, string category, int year, string format,
        string color, string initials, string description, double rating, int reads)
    {
        Id = id; Title = title; Author = author; Category = category; Year = year; Format = format;
        Color = color; Initials = initials; Description = description; Rating = rating; Reads = reads;
    }
}
