namespace OnLibrary.Models;

public sealed record DocumentReportRow(int Id, int DocumentId, string DocumentTitle, string Reporter, string Reason,
    string Details, string Status, DateTime CreatedAt, string? ResolvedBy);
