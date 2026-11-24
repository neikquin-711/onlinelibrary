using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IO.Compression;
using System.Text.RegularExpressions;
using OnLibrary.Services;
using OnLibrary.Data;
using OnLibrary.Models;

namespace OnLibrary.Controllers;

public class HomeController(LibraryDbContext db, IWebHostEnvironment environment, IFileScanner scanner,
    IPasswordHasher<LibraryUser> passwordHasher) : Controller
{
    private const long MaxUploadBytes = 25 * 1024 * 1024;
    private string DocumentStoragePath => Path.Combine(environment.ContentRootPath, "App_Data", "documents");
    public async Task<IActionResult> Index()
    {
        ViewBag.Categories = await db.LibraryReferences.AsNoTracking().Where(r => r.Type == "Category" && r.IsActive)
            .OrderBy(r => r.Name).ToListAsync();
        return View(await db.Documents.AsNoTracking().Where(d => d.IsApproved).OrderByDescending(d => d.Reads).ToListAsync());
    }

    public async Task<IActionResult> Catalog(string? q, string? category, string? author, int? yearFrom, int? yearTo, string? sort = "popular", int page = 1)
    {
        var query = db.Documents.AsNoTracking().Where(d => d.IsApproved).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(d => d.Title.Contains(term) || d.Author.Contains(term) || d.Category.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(category) && category != "Tất cả") query = query.Where(d => d.Category == category);
        if (!string.IsNullOrWhiteSpace(author)) query = query.Where(d => d.Author == author);
        if (yearFrom.HasValue) query = query.Where(d => d.Year >= yearFrom.Value);
        if (yearTo.HasValue) query = query.Where(d => d.Year <= yearTo.Value);
        query = sort switch { "newest" => query.OrderByDescending(d => d.Year), "rating" => query.OrderByDescending(d => d.Rating), _ => query.OrderByDescending(d => d.Reads) };
        const int pageSize = 12;
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var documents = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewBag.Categories = await db.LibraryReferences.AsNoTracking().Where(r => r.Type == "Category" && r.IsActive)
            .OrderBy(r => r.Name).Select(r => r.Name).ToListAsync();
        ViewBag.Authors = await db.LibraryReferences.AsNoTracking().Where(r => r.Type == "Author" && r.IsActive)
            .OrderBy(r => r.Name).Select(r => r.Name).ToListAsync();
        return View(new CatalogViewModel(documents, q, category ?? "Tất cả", sort ?? "popular", page, totalPages, total, author, yearFrom, yearTo));
    }

    public async Task<IActionResult> Details(int id)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && (d.IsApproved || User.IsInRole("Admin") || User.IsInRole("Librarian")));
        if (document is null) return NotFound();
        ViewBag.Reviews = await db.Reviews.AsNoTracking().Where(r => r.DocumentId == id && !r.IsHidden)
            .OrderByDescending(r => r.UpdatedAt).Take(50)
            .Join(db.Users, review => review.UserId, user => user.Id,
                (review, user) => new ReviewCard(user.DisplayName, review.Rating, review.Comment, review.UpdatedAt))
            .ToListAsync();
        var reviewCount = await db.Reviews.CountAsync(r => r.DocumentId == id && !r.IsHidden);
        ViewBag.ReviewCount = reviewCount;
        ViewBag.AverageRating = reviewCount == 0 ? document.Rating : await db.Reviews.Where(r => r.DocumentId == id && !r.IsHidden).AverageAsync(r => (double)r.Rating);
        ViewBag.IsFavorite = false;
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            ViewBag.IsFavorite = await db.Favorites.AnyAsync(f => f.UserId == userId && f.DocumentId == id);
            ViewBag.MyReview = await db.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId && r.DocumentId == id);
        }
        return View(document);
    }

    public async Task<IActionResult> Read(int id)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && (d.IsApproved || User.IsInRole("Admin") || User.IsInRole("Librarian")));
        if (document is not null)
        {
            var entry = db.Entry(document);
            entry.Property(d => d.Reads).CurrentValue++;
            if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                var history = await db.ReadingHistory.SingleOrDefaultAsync(h => h.UserId == userId && h.DocumentId == id);
                if (history is null) db.ReadingHistory.Add(new ReadingHistory { UserId = userId, DocumentId = id, LastReadAt = DateTime.UtcNow, OpenCount = 1 });
                else { history.LastReadAt = DateTime.UtcNow; history.OpenCount++; }
            }
            await db.SaveChangesAsync();
        }
        return document is null ? NotFound() : View(document);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Favorite(int id)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await db.Documents.AnyAsync(d => d.Id == id)) return NotFound();
        var favorite = await db.Favorites.FindAsync(userId, id);
        if (favorite is null) db.Favorites.Add(new LibraryFavorite { UserId = userId, DocumentId = id });
        else db.Favorites.Remove(favorite);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, int rating, string? comment)
    {
        if (rating is < 1 or > 5 || string.IsNullOrWhiteSpace(comment) || comment.Trim().Length > 1000)
        {
            TempData["ReviewError"] = "Chọn số sao từ 1 đến 5 và nhập nhận xét tối đa 1.000 ký tự.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!await db.Documents.AnyAsync(d => d.Id == id)) return NotFound();
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.UserId == userId && r.DocumentId == id);
        if (review is null) db.Reviews.Add(new LibraryReview { UserId = userId, DocumentId = id, Rating = rating, Comment = comment.Trim() });
        else { review.Rating = rating; review.Comment = comment.Trim(); review.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync();
        var average = await db.Reviews.Where(r => r.DocumentId == id && !r.IsHidden).AverageAsync(r => (double)r.Rating);
        var document = await db.Documents.FirstAsync(d => d.Id == id);
        db.Entry(document).Property(d => d.Rating).CurrentValue = average;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = "Admin,Librarian")]
    public async Task<IActionResult> ReviewModeration()
    {
        var reviews = await db.Reviews.AsNoTracking().OrderByDescending(review => review.UpdatedAt).Take(200)
            .Join(db.Documents, review => review.DocumentId, document => document.Id,
                (review, document) => new { review, document.Title })
            .Join(db.Users, item => item.review.UserId, user => user.Id,
                (item, user) => new ReviewModerationRow(item.review.Id, item.review.DocumentId, item.Title,
                    user.DisplayName, item.review.Rating, item.review.Comment, item.review.UpdatedAt, item.review.IsHidden))
            .ToListAsync();
        return View(reviews);
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetReviewVisibility(int id, bool hide)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id);
        if (review is null) return NotFound();
        review.IsHidden = hide;
        await db.SaveChangesAsync();
        var average = await db.Reviews.Where(r => r.DocumentId == review.DocumentId && !r.IsHidden)
            .Select(r => (double?)r.Rating).AverageAsync() ?? 0;
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == review.DocumentId);
        if (document is not null) { document.Rating = average; await db.SaveChangesAsync(); }
        TempData["ReviewAdminNotice"] = hide ? "Đã ẩn nhận xét khỏi trang tài liệu." : "Đã hiện nhận xét trên trang tài liệu.";
        return RedirectToAction(nameof(ReviewModeration));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitReport(int id, string? reason, string? details)
    {
        var allowedReasons = new[] { "Tệp bị lỗi", "Thông tin chưa chính xác", "Nội dung vi phạm", "Khác" };
        if (reason is null || !allowedReasons.Contains(reason) || string.IsNullOrWhiteSpace(details) || details.Trim().Length > 1000)
        {
            TempData["ReportError"] = "Chọn lý do và mô tả vấn đề (tối đa 1.000 ký tự).";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!await db.Documents.AnyAsync(d => d.Id == id)) return NotFound();
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (await db.DocumentReports.AnyAsync(r => r.UserId == userId && r.DocumentId == id && r.Status == "Pending"))
        {
            TempData["ReportError"] = "Bạn đã gửi báo cáo đang chờ xử lý cho tài liệu này.";
            return RedirectToAction(nameof(Details), new { id });
        }
        db.DocumentReports.Add(new DocumentReport
        {
            UserId = userId, DocumentId = id, Reason = reason, Details = details.Trim(), Status = "Pending"
        });
        await db.SaveChangesAsync();
        TempData["ReportNotice"] = "Cảm ơn bạn. Báo cáo đã được gửi đến thủ thư.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = "Admin,Librarian")]
    public async Task<IActionResult> Reports()
    {
        var reports = await db.DocumentReports.AsNoTracking()
            .OrderBy(report => report.Status == "Pending" ? 0 : 1)
            .ThenByDescending(report => report.CreatedAt).Take(200)
            .Join(db.Documents, report => report.DocumentId, document => document.Id,
                (report, document) => new { report, document.Title })
            .Join(db.Users, item => item.report.UserId, user => user.Id,
                (item, user) => new DocumentReportRow(item.report.Id, item.report.DocumentId, item.Title, user.DisplayName,
                    item.report.Reason, item.report.Details, item.report.Status, item.report.CreatedAt,
                    db.Users.Where(resolver => resolver.Id == item.report.ResolvedByUserId)
                        .Select(resolver => resolver.DisplayName).FirstOrDefault()))
            .ToListAsync();
        return View(reports);
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveReport(int reportId, string? decision)
    {
        if (decision is not ("Resolved" or "Rejected")) return BadRequest();
        var report = await db.DocumentReports.FirstOrDefaultAsync(r => r.Id == reportId);
        if (report is null) return NotFound();
        if (report.Status != "Pending")
        {
            TempData["ReportAdminError"] = "Báo cáo này đã được xử lý trước đó.";
            return RedirectToAction(nameof(Reports));
        }
        report.Status = decision;
        report.ResolvedAt = DateTime.UtcNow;
        report.ResolvedByUserId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await db.SaveChangesAsync();
        await RecordAdminActionAsync("Resolve report", "DocumentReport", reportId, $"Set report status to {decision}.");
        TempData["ReportAdminNotice"] = "Đã cập nhật trạng thái báo cáo.";
        return RedirectToAction(nameof(Reports));
    }

    [Authorize]
    public async Task<IActionResult> MyLibrary()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var favorites = await db.Favorites.Where(f => f.UserId == userId).Join(db.Documents,
            favorite => favorite.DocumentId, document => document.Id, (_, document) => document).AsNoTracking().ToListAsync();
        var history = await db.ReadingHistory.Where(h => h.UserId == userId).OrderByDescending(h => h.LastReadAt)
            .Join(db.Documents, item => item.DocumentId, document => document.Id,
                (item, document) => new LibraryHistoryItem(document, item.LastReadAt, item.OpenCount)).Take(50).AsNoTracking().ToListAsync();
        var downloads = await db.DocumentDownloads.Where(item => item.UserId == userId).OrderByDescending(item => item.DownloadedAt)
            .Join(db.Documents, item => item.DocumentId, document => document.Id,
                (item, document) => new LibraryDownloadItem(document, item.DownloadedAt)).Take(50).AsNoTracking().ToListAsync();
        return View(new MyLibraryViewModel(favorites, history, downloads));
    }

    [HttpGet]
    public async Task<IActionResult> DocumentFile(int id, bool download = false)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && (d.IsApproved || User.IsInRole("Admin") || User.IsInRole("Librarian")));
        if (document?.StoredFileName is null) return NotFound("Tài liệu này chưa có tệp đọc.");
        var contentType = document.Format switch
        {
            "PDF" => "application/pdf",
            "EPUB" => "application/epub+zip",
            "DOCX" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => null
        };
        if (contentType is null) return NotFound();
        var safeName = Path.GetFileName(document.StoredFileName);
        if (safeName != document.StoredFileName || safeName != Path.GetFileNameWithoutExtension(safeName) + Path.GetExtension(safeName)) return NotFound();
        var path = Path.Combine(DocumentStoragePath, safeName);
        if (!System.IO.File.Exists(path)) return NotFound("Không tìm thấy tệp tài liệu.");
        if (download && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            db.DocumentDownloads.Add(new DocumentDownload { UserId = userId, DocumentId = id, DownloadedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var downloadName = $"{document.Title}{Path.GetExtension(safeName)}";
        return download
            ? File(stream, contentType, downloadName, enableRangeProcessing: true)
            : File(stream, contentType, enableRangeProcessing: true);
    }

    [Authorize(Roles = "Admin,Librarian")]
    public async Task<IActionResult> Admin()
    {
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var documents = await db.Documents.AsNoTracking().OrderByDescending(d => d.Id).ToListAsync();
        var model = new AdminDashboardViewModel(documents, documents.Count,
            await db.Users.CountAsync(u => u.Role == "Reader"),
            await db.ReadingHistory.Where(h => h.LastReadAt >= monthStart).SumAsync(h => (long?)h.OpenCount) ?? 0,
            await db.DocumentDownloads.CountAsync(d => d.DownloadedAt >= monthStart),
            await db.DocumentReports.CountAsync(r => r.Status == "Pending"),
            await db.Documents.CountAsync(d => !d.IsApproved && !d.IsRejected));
        return View(model);
    }

    [Authorize(Roles = "Admin,Librarian")]
    public async Task<IActionResult> DocumentModeration() => View(await db.Documents.AsNoTracking()
        .Where(document => !document.IsApproved && !document.IsRejected).OrderBy(document => document.Id).ToListAsync());

    [Authorize(Roles = "Admin,Librarian")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDocumentApproval(int id, bool approve)
    {
        var document = await db.Documents.FirstOrDefaultAsync(item => item.Id == id);
        if (document is null) return NotFound();
        document.IsApproved = approve;
        document.IsRejected = !approve;
        await db.SaveChangesAsync();
        await RecordAdminActionAsync(approve ? "Approve document" : "Reject document", "Document", id,
            $"{(approve ? "Approved" : "Rejected")} document: {document.Title}.");
        TempData["DocumentModerationNotice"] = approve ? "Đã duyệt tài liệu và đưa vào danh mục." : "Đã từ chối tài liệu; tài liệu vẫn được lưu trong kho quản trị.";
        return RedirectToAction(nameof(DocumentModeration));
    }

    [Authorize(Roles = "Admin,Librarian")]
    public async Task<IActionResult> ExportStatistics()
    {
        var documents = await db.Documents.AsNoTracking().OrderBy(d => d.Title).ToListAsync();
        var downloadCounts = await db.DocumentDownloads.AsNoTracking().GroupBy(d => d.DocumentId)
            .Select(group => new { DocumentId = group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.DocumentId, x => x.Count);
        var reviewStats = await db.Reviews.AsNoTracking().Where(r => !r.IsHidden).GroupBy(r => r.DocumentId)
            .Select(group => new { DocumentId = group.Key, Average = group.Average(r => (double)r.Rating), Count = group.Count() })
            .ToDictionaryAsync(x => x.DocumentId);
        static string Csv(string? value)
        {
            var safe = value ?? "";
            if (safe.Length > 0 && "=+-@\t\r".Contains(safe[0])) safe = "'" + safe;
            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("Tài liệu,Tác giả,Danh mục,Năm,Lượt đọc,Lượt tải xuống,Điểm đánh giá,Số đánh giá");
        foreach (var document in documents)
        {
            reviewStats.TryGetValue(document.Id, out var review);
            builder.Append(Csv(document.Title)).Append(',').Append(Csv(document.Author)).Append(',')
                .Append(Csv(document.Category)).Append(',').Append(document.Year).Append(',').Append(document.Reads).Append(',')
                .Append(downloadCounts.GetValueOrDefault(document.Id)).Append(',')
                .Append(review?.Average.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "0.00").Append(',')
                .AppendLine((review?.Count ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"onlibrary-statistics-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Users() => View(await db.Users.AsNoTracking()
        .OrderBy(u => u.Role).ThenBy(u => u.DisplayName).ToListAsync());

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(string? displayName, string? email, string? password, string? role)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 80 || normalizedEmail is null ||
            normalizedEmail.Length > 254 || !Regex.IsMatch(normalizedEmail, @"^[^\s@]+@[^\s@]+\.[^\s@]+$") ||
            role is not ("Reader" or "Librarian") || password is not { Length: >= 10 and <= 128 } ||
            !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
        {
            TempData["UserAdminError"] = "Thông tin chưa hợp lệ. Mật khẩu cần ít nhất 10 ký tự, có chữ hoa, chữ thường và chữ số.";
            return RedirectToAction(nameof(Users));
        }
        if (await db.Users.AnyAsync(user => user.Email == normalizedEmail))
        {
            TempData["UserAdminError"] = "Email này đã được đăng ký.";
            return RedirectToAction(nameof(Users));
        }
        var user = new LibraryUser { Email = normalizedEmail, DisplayName = displayName.Trim(), Role = role };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await RecordAdminActionAsync("Create user", "User", user.Id, $"Created {role} account {user.Email}.");
        TempData["UserAdminNotice"] = "Đã tạo tài khoản.";
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> EditUser(int id)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
        return user is null ? NotFound() : View(user);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(int id, string? displayName, string? email, string? role, string? newPassword)
    {
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id);
        if (user is null) return NotFound();
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        if (user.Role == "Admin" || string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 80 ||
            normalizedEmail is null || normalizedEmail.Length > 254 ||
            !Regex.IsMatch(normalizedEmail, @"^[^\s@]+@[^\s@]+\.[^\s@]+$") || role is not ("Reader" or "Librarian"))
        {
            ViewBag.Error = "Thông tin không hợp lệ hoặc tài khoản Admin được bảo vệ.";
            return View(user);
        }
        if (await db.Users.AnyAsync(item => item.Id != id && item.Email == normalizedEmail))
        {
            ViewBag.Error = "Email này đã được tài khoản khác sử dụng.";
            return View(user);
        }
        if (!string.IsNullOrEmpty(newPassword))
        {
            if (newPassword.Length is < 10 or > 128 || !newPassword.Any(char.IsUpper) || !newPassword.Any(char.IsLower) || !newPassword.Any(char.IsDigit))
            {
                ViewBag.Error = "Mật khẩu mới cần ít nhất 10 ký tự, có chữ hoa, chữ thường và chữ số.";
                return View(user);
            }
            user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        }
        user.DisplayName = displayName.Trim();
        user.Email = normalizedEmail;
        user.Role = role;
        await db.SaveChangesAsync();
        await RecordAdminActionAsync("Update user", "User", id, $"Updated account {user.Email}.");
        TempData["UserAdminNotice"] = "Đã cập nhật tài khoản.";
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(int id)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) || actorId == id)
        {
            TempData["UserAdminError"] = "Không thể xóa tài khoản đang sử dụng.";
            return RedirectToAction(nameof(Users));
        }
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == id);
        if (user is null) return NotFound();
        if (user.Role == "Admin")
        {
            TempData["UserAdminError"] = "Tài khoản Admin được bảo vệ.";
            return RedirectToAction(nameof(Users));
        }
        var email = user.Email;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var reviewDocumentIds = await db.Reviews.Where(review => review.UserId == id).Select(review => review.DocumentId).ToListAsync();
        db.Favorites.RemoveRange(db.Favorites.Where(item => item.UserId == id));
        db.Reviews.RemoveRange(db.Reviews.Where(item => item.UserId == id));
        db.ReadingHistory.RemoveRange(db.ReadingHistory.Where(item => item.UserId == id));
        db.DocumentDownloads.RemoveRange(db.DocumentDownloads.Where(item => item.UserId == id));
        db.DocumentReports.RemoveRange(db.DocumentReports.Where(item => item.UserId == id));
        var resolvedReports = await db.DocumentReports.Where(item => item.ResolvedByUserId == id).ToListAsync();
        foreach (var report in resolvedReports) report.ResolvedByUserId = null;
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        foreach (var documentId in reviewDocumentIds.Distinct())
        {
            var average = await db.Reviews.Where(review => review.DocumentId == documentId && !review.IsHidden)
                .Select(review => (double?)review.Rating).AverageAsync() ?? 0;
            var document = await db.Documents.FirstOrDefaultAsync(item => item.Id == documentId);
            if (document is not null) document.Rating = average;
        }
        await db.SaveChangesAsync();
        await RecordAdminActionAsync("Delete user", "User", id, $"Deleted account {email} and associated reader activity.");
        await transaction.CommitAsync();
        TempData["UserAdminNotice"] = "Đã xóa tài khoản và dữ liệu cá nhân liên quan.";
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AuditLog() => View(await db.AdminAuditLogs.AsNoTracking()
        .OrderByDescending(log => log.CreatedAt).Take(250).ToListAsync());

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Settings() => View(await db.SystemSettings.AsNoTracking().FirstAsync(settings => settings.Id == 1));

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(bool registrationEnabled)
    {
        var settings = await db.SystemSettings.FirstAsync(item => item.Id == 1);
        settings.RegistrationEnabled = registrationEnabled;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedBy = User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        await db.SaveChangesAsync();
        await RecordAdminActionAsync("Update settings", "System", settings.Id,
            $"Public registration {(registrationEnabled ? "enabled" : "disabled")}.");
        TempData["SettingsNotice"] = "Đã lưu cấu hình hệ thống.";
        return RedirectToAction(nameof(Settings));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetUserRole(int id, string? role)
    {
        if (role is not ("Reader" or "Librarian"))
        {
            TempData["UserAdminError"] = "Chỉ có thể gán vai trò Độc giả hoặc Thủ thư từ trang này.";
            return RedirectToAction(nameof(Users));
        }
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (user.Role == "Admin")
        {
            TempData["UserAdminError"] = "Tài khoản Admin được bảo vệ và không thể đổi vai trò tại đây.";
            return RedirectToAction(nameof(Users));
        }
        user.Role = role;
        await db.SaveChangesAsync();
        await RecordAdminActionAsync("Update role", "User", id, $"Changed role for {user.Email} to {role}.");
        TempData["UserAdminNotice"] = $"Đã cập nhật vai trò của {user.DisplayName}.";
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Roles = "Admin,Librarian")]
    public async Task<IActionResult> References() => View(await db.LibraryReferences.AsNoTracking()
        .OrderBy(reference => reference.Type).ThenBy(reference => reference.Name).ToListAsync());

    [Authorize(Roles = "Admin,Librarian")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddReference(string? type, string? name, string? description)
    {
        if (!IsReferenceType(type) || string.IsNullOrWhiteSpace(name) || name.Trim().Length > ReferenceNameLimit(type) || (description?.Length ?? 0) > 500)
            TempData["ReferenceError"] = "Chọn loại hợp lệ; tên và mô tả phải nằm trong giới hạn ký tự cho phép.";
        else if (await db.LibraryReferences.AnyAsync(r => r.Type == type && r.Name == name.Trim()))
            TempData["ReferenceError"] = "Tên này đã tồn tại trong danh mục.";
        else
        {
            db.LibraryReferences.Add(new LibraryReference { Type = type!, Name = name.Trim(), Description = description?.Trim() ?? "" });
            await db.SaveChangesAsync();
            await RecordAdminActionAsync("Add reference", type!, null, $"Added reference: {name.Trim()}.");
            TempData["ReferenceNotice"] = "Đã thêm mục thư viện.";
        }
        return RedirectToAction(nameof(References));
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameReference(int id, string? name, string? description)
    {
        var reference = await db.LibraryReferences.FirstOrDefaultAsync(r => r.Id == id);
        if (reference is null) return NotFound();
        var newName = name?.Trim();
        if (string.IsNullOrWhiteSpace(newName) || newName.Length > ReferenceNameLimit(reference.Type) || (description?.Length ?? 0) > 500)
            TempData["ReferenceError"] = "Tên bắt buộc và phải nằm trong giới hạn ký tự của loại mục.";
        else if (await db.LibraryReferences.AnyAsync(r => r.Type == reference.Type && r.Name == newName && r.Id != id))
            TempData["ReferenceError"] = "Tên này đã tồn tại trong nhóm.";
        else
        {
            var oldName = reference.Name;
            var documents = await db.Documents.Where(document => reference.Type == "Category" ? document.Category == oldName :
                reference.Type == "Author" ? document.Author == oldName : document.Publisher == oldName).ToListAsync();
            foreach (var document in documents)
            {
                if (reference.Type == "Category") document.Category = newName;
                else if (reference.Type == "Author") document.Author = newName;
                else document.Publisher = newName;
            }
            reference.Name = newName;
            reference.Description = description?.Trim() ?? "";
            await db.SaveChangesAsync();
            await RecordAdminActionAsync("Update reference", reference.Type, reference.Id, $"Updated reference: {reference.Name}.");
            TempData["ReferenceNotice"] = "Đã cập nhật mục và đồng bộ các tài liệu đang sử dụng mục này.";
        }
        return RedirectToAction(nameof(References));
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReference(int id)
    {
        var reference = await db.LibraryReferences.FirstOrDefaultAsync(r => r.Id == id);
        if (reference is null) return NotFound();
        var inUse = await db.Documents.AnyAsync(document => reference.Type == "Category" ? document.Category == reference.Name :
            reference.Type == "Author" ? document.Author == reference.Name : document.Publisher == reference.Name);
        if (inUse) TempData["ReferenceError"] = "Mục này đang được tài liệu sử dụng. Hãy sửa tài liệu trước khi xóa.";
        else
        {
            db.LibraryReferences.Remove(reference);
            await db.SaveChangesAsync();
            await RecordAdminActionAsync("Delete reference", reference.Type, reference.Id, $"Deleted reference: {reference.Name}.");
            TempData["ReferenceNotice"] = "Đã xóa mục thư viện.";
        }
        return RedirectToAction(nameof(References));
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateReferenceOptionsAsync();
        return View();
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        await PopulateReferenceOptionsAsync();
        return document is null ? NotFound() : View(document);
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string? title, string? author, string? category, string? publisher, int year,
        string? format, string? description, IFormFile? file)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (document is null) return NotFound();
        var formats = new[] { "PDF", "EPUB", "DOCX" };
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120 || string.IsNullOrWhiteSpace(author) || author.Length > 100 ||
            category is null || !await ReferenceExistsAsync("Category", category) || !await ReferenceExistsAsync("Author", author) ||
            !await ReferenceExistsAsync("Publisher", publisher) || year < 1000 || year > DateTime.UtcNow.Year ||
            format is null || !formats.Contains(format) || string.IsNullOrWhiteSpace(description) || description.Length > 1000 ||
            (file is not null && file.Length is <= 0 or > MaxUploadBytes) ||
            (file is null && document.StoredFileName is not null && document.Format != format))
        {
            ViewBag.Error = "Kiểm tra các trường bắt buộc, năm xuất bản và tệp (tối đa 25 MB). Nếu đổi định dạng, hãy tải lên tệp thay thế.";
            await PopulateReferenceOptionsAsync();
            return View(document);
        }

        string? newFileName = null;
        string? temporaryPath = null;
        string? newFilePath = null;
        var previousFileName = document.StoredFileName;
        try
        {
            if (file is not null)
            {
                var (mimeType, extension) = GetFileMetadata(format!);
                if (!string.Equals(Path.GetExtension(file.FileName), extension, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Phần mở rộng tệp không khớp với định dạng đã chọn.");
                if (!string.Equals(file.ContentType, mimeType, StringComparison.OrdinalIgnoreCase) &&
                    !(format == "EPUB" && file.ContentType == "application/octet-stream"))
                    throw new InvalidDataException("Kiểu nội dung tệp không khớp với định dạng đã chọn.");
                await ValidateDocumentFileAsync(file, format!);

                Directory.CreateDirectory(DocumentStoragePath);
                newFileName = $"{Guid.NewGuid():N}{extension}";
                temporaryPath = Path.Combine(DocumentStoragePath, $"{Guid.NewGuid():N}.upload");
                newFilePath = Path.Combine(DocumentStoragePath, newFileName);
                await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                    await file.CopyToAsync(output, HttpContext.RequestAborted);
                await scanner.ScanAsync(temporaryPath, HttpContext.RequestAborted);
                System.IO.File.Move(temporaryPath, newFilePath);
                temporaryPath = null;
                document.StoredFileName = newFileName;
                document.FileMimeType = mimeType;
                document.FileSizeBytes = file.Length;
            }

            document.Title = title.Trim();
            document.Author = author.Trim();
            document.Category = category!;
            document.Publisher = publisher!;
            document.Year = year;
            document.Format = format!;
            document.Initials = new string(title.Trim().Take(2).ToArray()).ToUpperInvariant();
            document.Description = description.Trim();
            document.IsApproved = false;
            document.IsRejected = false;
            await db.SaveChangesAsync();

        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or TimeoutException or IOException or DbUpdateException)
        {
            if (temporaryPath is not null && System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath);
            if (newFilePath is not null && System.IO.File.Exists(newFilePath)) System.IO.File.Delete(newFilePath);
            await db.Entry(document).ReloadAsync();
            ViewBag.Error = ex is InvalidDataException ? ex.Message : ex is TimeoutException
                ? "Quét bảo mật quá thời gian. Tài liệu chưa được cập nhật."
                : ex is InvalidOperationException ? ex.Message : "Không thể cập nhật tài liệu. Kiểm tra cấu hình quét virus và dung lượng lưu trữ.";
            await PopulateReferenceOptionsAsync();
            return View(document);
        }
        if (newFileName is not null && previousFileName is not null && Path.GetFileName(previousFileName) == previousFileName)
        {
            var oldPath = Path.Combine(DocumentStoragePath, previousFileName);
            try { if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath); }
            catch (IOException) { /* The update is committed; keep the orphaned old file for manual cleanup. */ }
        }
        return RedirectToAction(nameof(Admin));
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? title, string? author, string? category, string? publisher,
        int year, string? format, string? description, IFormFile? file)
    {
        var formats = new[] { "PDF", "EPUB", "DOCX" };
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120 || string.IsNullOrWhiteSpace(author) || author.Length > 100 ||
            category is null || !await ReferenceExistsAsync("Category", category) || !await ReferenceExistsAsync("Author", author) ||
            !await ReferenceExistsAsync("Publisher", publisher) || year < 1000 || year > DateTime.UtcNow.Year ||
            format is null || !formats.Contains(format) || string.IsNullOrWhiteSpace(description) || description.Length > 1000 ||
            file is null || file.Length is <= 0 or > MaxUploadBytes)
        {
            ViewBag.Error = "Kiểm tra các trường bắt buộc, năm xuất bản và tệp (tối đa 25 MB).";
            await PopulateReferenceOptionsAsync();
            return View();
        }
        var expectedMime = format switch
        {
            "PDF" => "application/pdf",
            "EPUB" => "application/epub+zip",
            _ => "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };
        var expectedExtension = format.ToLowerInvariant() switch { "epub" => ".epub", "docx" => ".docx", _ => ".pdf" };
        if (!string.Equals(Path.GetExtension(file.FileName), expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            ViewBag.Error = "Phần mở rộng tệp không khớp với định dạng đã chọn.";
            await PopulateReferenceOptionsAsync();
            return View();
        }
        if (!string.Equals(file.ContentType, expectedMime, StringComparison.OrdinalIgnoreCase) &&
            !(format == "EPUB" && file.ContentType == "application/octet-stream"))
        {
            ViewBag.Error = "Kiểu nội dung tệp không khớp với định dạng đã chọn.";
            await PopulateReferenceOptionsAsync();
            return View();
        }
        try { await ValidateDocumentFileAsync(file, format); }
        catch (InvalidDataException ex) { ViewBag.Error = ex.Message; await PopulateReferenceOptionsAsync(); return View(); }

        var count = await db.Documents.CountAsync();
        var colors = new[] { "sand", "blue", "coral", "green", "violet", "peach", "mint", "rose" };
        Directory.CreateDirectory(DocumentStoragePath);
        var storedName = $"{Guid.NewGuid():N}{expectedExtension}";
        var temporaryPath = Path.Combine(DocumentStoragePath, $"{Guid.NewGuid():N}.upload");
        var finalPath = Path.Combine(DocumentStoragePath, storedName);
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                await file.CopyToAsync(output, HttpContext.RequestAborted);
            await scanner.ScanAsync(temporaryPath, HttpContext.RequestAborted);
            System.IO.File.Move(temporaryPath, finalPath);
            db.Documents.Add(new LibraryDocument(0, title.Trim(), author.Trim(), category!, year, format,
                colors[count % colors.Length], new string(title.Trim().Take(2).ToArray()).ToUpperInvariant(), description.Trim(), 0, 0)
            { Publisher = publisher!, StoredFileName = storedName, FileMimeType = expectedMime, FileSizeBytes = file.Length, IsApproved = false });
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or TimeoutException or IOException or DbUpdateException)
        {
            if (System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath);
            if (System.IO.File.Exists(finalPath)) System.IO.File.Delete(finalPath);
            ViewBag.Error = ex is InvalidDataException ? ex.Message : ex is TimeoutException
                ? "Quét bảo mật quá thời gian. Tệp chưa được lưu."
                : ex is InvalidOperationException ? ex.Message : "Không thể lưu tệp. Kiểm tra cấu hình quét virus và dung lượng lưu trữ.";
            await PopulateReferenceOptionsAsync();
            return View();
        }
        return RedirectToAction(nameof(Admin));
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var document = await db.Documents.FindAsync(id);
        if (document is not null)
        {
            var storedName = document.StoredFileName;
            db.Documents.Remove(document);
            await db.SaveChangesAsync();
            if (storedName is not null && Path.GetFileName(storedName) == storedName)
            {
                var path = Path.Combine(DocumentStoragePath, storedName);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }
        return RedirectToAction(nameof(Admin));
    }

    private static async Task ValidateDocumentFileAsync(IFormFile file, string format)
    {
        await using var stream = file.OpenReadStream();
        if (format == "PDF")
        {
            var signature = new byte[5];
            if (await stream.ReadAsync(signature) != signature.Length || !signature.AsSpan().SequenceEqual("%PDF-"u8))
                throw new InvalidDataException("Tệp không có chữ ký PDF hợp lệ.");
            return;
        }

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count is 0 or > 5000 || archive.Entries.Sum(entry => entry.Length) > 150 * 1024 * 1024)
                throw new InvalidDataException("Cấu trúc tệp nén vượt giới hạn cho phép.");
            if (format == "EPUB")
            {
                var mimeEntry = archive.Entries.FirstOrDefault(entry => entry.FullName == "mimetype");
                if (archive.Entries[0].FullName != "mimetype" || mimeEntry is null || mimeEntry.Length > 64 || mimeEntry.CompressedLength != mimeEntry.Length)
                    throw new InvalidDataException("Tệp không có cấu trúc EPUB hợp lệ.");
                using var reader = new StreamReader(mimeEntry.Open());
                if (await reader.ReadToEndAsync() != "application/epub+zip") throw new InvalidDataException("Tệp không có chữ ký EPUB hợp lệ.");
            }
            else if (!archive.Entries.Any(entry => entry.FullName == "[Content_Types].xml") ||
                     !archive.Entries.Any(entry => entry.FullName == "word/document.xml"))
                throw new InvalidDataException("Tệp không có cấu trúc DOCX hợp lệ.");
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            throw new InvalidDataException("Tệp nén bị hỏng hoặc không đúng định dạng.");
        }
    }

    private static (string MimeType, string Extension) GetFileMetadata(string format) => format switch
    {
        "PDF" => ("application/pdf", ".pdf"),
        "EPUB" => ("application/epub+zip", ".epub"),
        "DOCX" => ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx"),
        _ => throw new InvalidDataException("Định dạng tệp không được hỗ trợ.")
    };

    private async Task PopulateReferenceOptionsAsync()
    {
        ViewBag.Categories = await db.LibraryReferences.AsNoTracking().Where(r => r.Type == "Category" && r.IsActive).OrderBy(r => r.Name).ToListAsync();
        ViewBag.Authors = await db.LibraryReferences.AsNoTracking().Where(r => r.Type == "Author" && r.IsActive).OrderBy(r => r.Name).ToListAsync();
        ViewBag.Publishers = await db.LibraryReferences.AsNoTracking().Where(r => r.Type == "Publisher" && r.IsActive).OrderBy(r => r.Name).ToListAsync();
    }

    private async Task<bool> ReferenceExistsAsync(string type, string? name) =>
        !string.IsNullOrWhiteSpace(name) && await db.LibraryReferences.AnyAsync(r => r.Type == type && r.Name == name.Trim() && r.IsActive);

    private static bool IsReferenceType(string? type) => type is "Category" or "Author" or "Publisher";
    private static int ReferenceNameLimit(string? type) => type switch { "Category" => 60, "Author" => 100, "Publisher" => 120, _ => 0 };

    private async Task RecordAdminActionAsync(string action, string entityType, int? entityId, string summary)
    {
        db.AdminAuditLogs.Add(new AdminAuditLog
        {
            ActorEmail = User.FindFirstValue(ClaimTypes.Email) ?? "unknown",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary.Length <= 500 ? summary : summary[..500]
        });
        await db.SaveChangesAsync();
    }
}
