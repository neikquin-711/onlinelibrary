using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IO.Compression;
using OnLibrary.Services;
using OnLibrary.Data;
using OnLibrary.Models;

namespace OnLibrary.Controllers;

public class HomeController(LibraryDbContext db, IWebHostEnvironment environment, IFileScanner scanner) : Controller
{
    private const long MaxUploadBytes = 25 * 1024 * 1024;
    private string DocumentStoragePath => Path.Combine(environment.ContentRootPath, "App_Data", "documents");
    public async Task<IActionResult> Index() => View(await db.Documents.AsNoTracking().OrderByDescending(d => d.Reads).ToListAsync());

    public async Task<IActionResult> Catalog(string? q, string? category, string? sort = "popular")
    {
        var query = db.Documents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(d => d.Title.Contains(term) || d.Author.Contains(term) || d.Category.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(category) && category != "Tất cả") query = query.Where(d => d.Category == category);
        query = sort switch { "newest" => query.OrderByDescending(d => d.Year), "rating" => query.OrderByDescending(d => d.Rating), _ => query.OrderByDescending(d => d.Reads) };
        ViewBag.Query = q; ViewBag.Category = category ?? "Tất cả"; ViewBag.Sort = sort;
        return View(await query.Take(100).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (document is null) return NotFound();
        ViewBag.Reviews = await db.Reviews.AsNoTracking().Where(r => r.DocumentId == id)
            .Join(db.Users, review => review.UserId, user => user.Id,
                (review, user) => new ReviewCard(user.DisplayName, review.Rating, review.Comment, review.UpdatedAt))
            .OrderByDescending(review => review.UpdatedAt).Take(50).ToListAsync();
        var reviewCount = await db.Reviews.CountAsync(r => r.DocumentId == id);
        ViewBag.ReviewCount = reviewCount;
        ViewBag.AverageRating = reviewCount == 0 ? document.Rating : await db.Reviews.Where(r => r.DocumentId == id).AverageAsync(r => (double)r.Rating);
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
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id);
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
        var average = await db.Reviews.Where(r => r.DocumentId == id).AverageAsync(r => (double)r.Rating);
        var document = await db.Documents.FirstAsync(d => d.Id == id);
        db.Entry(document).Property(d => d.Rating).CurrentValue = average;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
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
        return View(new MyLibraryViewModel(favorites, history));
    }

    [HttpGet]
    public async Task<IActionResult> DocumentFile(int id, bool download = false)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
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
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var downloadName = $"{document.Title}{Path.GetExtension(safeName)}";
        return download
            ? File(stream, contentType, downloadName, enableRangeProcessing: true)
            : File(stream, contentType, enableRangeProcessing: true);
    }

    [Authorize(Roles = "Admin,Librarian")]
    public async Task<IActionResult> Admin() => View(await db.Documents.AsNoTracking().OrderByDescending(d => d.Id).ToListAsync());

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Users() => View(await db.Users.AsNoTracking()
        .OrderBy(u => u.Role).ThenBy(u => u.DisplayName).ToListAsync());

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
        TempData["UserAdminNotice"] = $"Đã cập nhật vai trò của {user.DisplayName}.";
        return RedirectToAction(nameof(Users));
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpGet]
    public IActionResult Create() => View();

    [Authorize(Roles = "Admin,Librarian")]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        return document is null ? NotFound() : View(document);
    }

    [Authorize(Roles = "Admin,Librarian")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string? title, string? author, string? category, int year,
        string? format, string? description, IFormFile? file)
    {
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (document is null) return NotFound();
        var categories = new[] { "Văn học", "Tâm lý học", "Kỹ năng sống", "Lịch sử" };
        var formats = new[] { "PDF", "EPUB", "DOCX" };
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120 || string.IsNullOrWhiteSpace(author) || author.Length > 100 ||
            category is null || !categories.Contains(category) || year < 1000 || year > DateTime.UtcNow.Year ||
            format is null || !formats.Contains(format) || string.IsNullOrWhiteSpace(description) || description.Length > 1000 ||
            (file is not null && file.Length is <= 0 or > MaxUploadBytes) ||
            (file is null && document.StoredFileName is not null && document.Format != format))
        {
            ViewBag.Error = "Kiểm tra các trường bắt buộc, năm xuất bản và tệp (tối đa 25 MB). Nếu đổi định dạng, hãy tải lên tệp thay thế.";
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
            document.Year = year;
            document.Format = format!;
            document.Initials = new string(title.Trim().Take(2).ToArray()).ToUpperInvariant();
            document.Description = description.Trim();
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
    public async Task<IActionResult> Create(string? title, string? author, string? category, int year, string? format, string? description, IFormFile? file)
    {
        var categories = new[] { "Văn học", "Tâm lý học", "Kỹ năng sống", "Lịch sử" };
        var formats = new[] { "PDF", "EPUB", "DOCX" };
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120 || string.IsNullOrWhiteSpace(author) || author.Length > 100 ||
            category is null || !categories.Contains(category) || year < 1000 || year > DateTime.UtcNow.Year ||
            format is null || !formats.Contains(format) || string.IsNullOrWhiteSpace(description) || description.Length > 1000 ||
            file is null || file.Length is <= 0 or > MaxUploadBytes)
        {
            ViewBag.Error = "Kiểm tra các trường bắt buộc, năm xuất bản và tệp (tối đa 25 MB).";
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
            return View();
        }
        if (!string.Equals(file.ContentType, expectedMime, StringComparison.OrdinalIgnoreCase) &&
            !(format == "EPUB" && file.ContentType == "application/octet-stream"))
        {
            ViewBag.Error = "Kiểu nội dung tệp không khớp với định dạng đã chọn.";
            return View();
        }
        try { await ValidateDocumentFileAsync(file, format); }
        catch (InvalidDataException ex) { ViewBag.Error = ex.Message; return View(); }

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
            { StoredFileName = storedName, FileMimeType = expectedMime, FileSizeBytes = file.Length });
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or TimeoutException or IOException or DbUpdateException)
        {
            if (System.IO.File.Exists(temporaryPath)) System.IO.File.Delete(temporaryPath);
            if (System.IO.File.Exists(finalPath)) System.IO.File.Delete(finalPath);
            ViewBag.Error = ex is InvalidDataException ? ex.Message : ex is TimeoutException
                ? "Quét bảo mật quá thời gian. Tệp chưa được lưu."
                : ex is InvalidOperationException ? ex.Message : "Không thể lưu tệp. Kiểm tra cấu hình quét virus và dung lượng lưu trữ.";
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
}
