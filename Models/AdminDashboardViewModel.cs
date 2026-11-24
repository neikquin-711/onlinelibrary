namespace OnLibrary.Models;

public sealed record AdminDashboardViewModel(IReadOnlyList<LibraryDocument> Documents, int TotalDocuments,
    int TotalReaders, long ReadsThisMonth, int DownloadsThisMonth, int PendingReports, int PendingApprovals);
