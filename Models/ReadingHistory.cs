namespace OnLibrary.Models;

public sealed class ReadingHistory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int DocumentId { get; set; }
    public DateTime LastReadAt { get; set; } = DateTime.UtcNow;
    public int OpenCount { get; set; }
}
