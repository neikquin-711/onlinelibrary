namespace OnLibrary.Models;

public sealed class DocumentReport
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int DocumentId { get; set; }
    public string Reason { get; set; } = "";
    public string Details { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }
}
