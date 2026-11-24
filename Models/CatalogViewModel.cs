namespace OnLibrary.Models;

public sealed record CatalogViewModel(IReadOnlyList<LibraryDocument> Documents, string? Query,
    string Category, string Sort, int Page, int TotalPages, int TotalResults);
