namespace OnLibrary.Models;

public sealed record ReviewModerationRow(int Id, int DocumentId, string DocumentTitle, string Reviewer,
    int Rating, string Comment, DateTime UpdatedAt, bool IsHidden);
