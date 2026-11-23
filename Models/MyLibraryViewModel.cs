namespace OnLibrary.Models;

public sealed record LibraryHistoryItem(LibraryDocument Document, DateTime LastReadAt, int OpenCount);

public sealed record MyLibraryViewModel(IReadOnlyList<LibraryDocument> Favorites, IReadOnlyList<LibraryHistoryItem> History);
