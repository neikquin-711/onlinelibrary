namespace OnLibrary.Models;

public sealed class LibraryFavorite
{
    public int UserId { get; set; }
    public int DocumentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
