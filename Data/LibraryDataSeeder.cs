using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnLibrary.Models;

namespace OnLibrary.Data;

public static class LibraryDataSeeder
{
    public static async Task InitializeAsync(LibraryDbContext db, IPasswordHasher<LibraryUser> hasher, IConfiguration configuration)
    {
        await db.Database.EnsureCreatedAsync();
        if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Documents', 'StoredFileName') IS NULL ALTER TABLE [Documents] ADD [StoredFileName] nvarchar(80) NULL;");
            await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Documents', 'FileMimeType') IS NULL ALTER TABLE [Documents] ADD [FileMimeType] nvarchar(100) NULL;");
            await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Documents', 'FileSizeBytes') IS NULL ALTER TABLE [Documents] ADD [FileSizeBytes] bigint NULL;");
            await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Documents', 'Publisher') IS NULL ALTER TABLE [Documents] ADD [Publisher] nvarchar(120) NOT NULL CONSTRAINT [DF_Documents_Publisher] DEFAULT N'';");
            await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Reviews', 'IsHidden') IS NULL ALTER TABLE [Reviews] ADD [IsHidden] bit NOT NULL CONSTRAINT [DF_Reviews_IsHidden] DEFAULT 0;");
            await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Documents', 'IsApproved') IS NULL ALTER TABLE [Documents] ADD [IsApproved] bit NOT NULL CONSTRAINT [DF_Documents_IsApproved] DEFAULT 1;");
            await db.Database.ExecuteSqlRawAsync("IF COL_LENGTH('Documents', 'IsRejected') IS NULL ALTER TABLE [Documents] ADD [IsRejected] bit NOT NULL CONSTRAINT [DF_Documents_IsRejected] DEFAULT 0;");
            await db.Database.ExecuteSqlRawAsync("""IF OBJECT_ID(N'dbo.AdminAuditLogs', N'U') IS NULL CREATE TABLE [dbo].[AdminAuditLogs] ([Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AdminAuditLogs] PRIMARY KEY, [ActorEmail] nvarchar(254) NOT NULL, [Action] nvarchar(40) NOT NULL, [EntityType] nvarchar(40) NOT NULL, [EntityId] int NULL, [Summary] nvarchar(500) NOT NULL, [CreatedAt] datetime2 NOT NULL);""");
            await db.Database.ExecuteSqlRawAsync("""IF OBJECT_ID(N'dbo.LibrarySystemSettings', N'U') IS NULL CREATE TABLE [dbo].[LibrarySystemSettings] ([Id] int NOT NULL CONSTRAINT [PK_LibrarySystemSettings] PRIMARY KEY, [RegistrationEnabled] bit NOT NULL, [UpdatedAt] datetime2 NOT NULL, [UpdatedBy] nvarchar(254) NOT NULL);""");
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.Favorites', N'U') IS NULL CREATE TABLE [dbo].[Favorites] ([UserId] int NOT NULL, [DocumentId] int NOT NULL, [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Favorites_CreatedAt] DEFAULT SYSUTCDATETIME(), CONSTRAINT [PK_Favorites] PRIMARY KEY ([UserId], [DocumentId]), CONSTRAINT [FK_Favorites_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE, CONSTRAINT [FK_Favorites_Documents] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE CASCADE);");
            await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Favorites_DocumentId' AND object_id = OBJECT_ID(N'dbo.Favorites')) CREATE INDEX [IX_Favorites_DocumentId] ON [dbo].[Favorites] ([DocumentId]);");
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.Reviews', N'U') IS NULL CREATE TABLE [dbo].[Reviews] ([Id] int IDENTITY(1,1) NOT NULL, [UserId] int NOT NULL, [DocumentId] int NOT NULL, [Rating] int NOT NULL, [Comment] nvarchar(1000) NOT NULL, [CreatedAt] datetime2 NOT NULL, [UpdatedAt] datetime2 NOT NULL, CONSTRAINT [PK_Reviews] PRIMARY KEY ([Id]), CONSTRAINT [FK_Reviews_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE, CONSTRAINT [FK_Reviews_Documents] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE CASCADE);");
            await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Reviews_UserId_DocumentId' AND object_id = OBJECT_ID(N'dbo.Reviews')) CREATE UNIQUE INDEX [IX_Reviews_UserId_DocumentId] ON [dbo].[Reviews] ([UserId], [DocumentId]);");
            await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Reviews_DocumentId' AND object_id = OBJECT_ID(N'dbo.Reviews')) CREATE INDEX [IX_Reviews_DocumentId] ON [dbo].[Reviews] ([DocumentId]);");
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.ReadingHistory', N'U') IS NULL CREATE TABLE [dbo].[ReadingHistory] ([Id] int IDENTITY(1,1) NOT NULL, [UserId] int NOT NULL, [DocumentId] int NOT NULL, [LastReadAt] datetime2 NOT NULL, [OpenCount] int NOT NULL, CONSTRAINT [PK_ReadingHistory] PRIMARY KEY ([Id]), CONSTRAINT [FK_ReadingHistory_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE, CONSTRAINT [FK_ReadingHistory_Documents] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE CASCADE);");
            await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReadingHistory_UserId_DocumentId' AND object_id = OBJECT_ID(N'dbo.ReadingHistory')) CREATE UNIQUE INDEX [IX_ReadingHistory_UserId_DocumentId] ON [dbo].[ReadingHistory] ([UserId], [DocumentId]);");
            await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReadingHistory_UserId_LastReadAt' AND object_id = OBJECT_ID(N'dbo.ReadingHistory')) CREATE INDEX [IX_ReadingHistory_UserId_LastReadAt] ON [dbo].[ReadingHistory] ([UserId], [LastReadAt]);");
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.DocumentReports', N'U') IS NULL CREATE TABLE [dbo].[DocumentReports] ([Id] int IDENTITY(1,1) NOT NULL, [UserId] int NOT NULL, [DocumentId] int NOT NULL, [Reason] nvarchar(50) NOT NULL, [Details] nvarchar(1000) NOT NULL, [Status] nvarchar(20) NOT NULL, [CreatedAt] datetime2 NOT NULL, [ResolvedAt] datetime2 NULL, [ResolvedByUserId] int NULL, CONSTRAINT [PK_DocumentReports] PRIMARY KEY ([Id]), CONSTRAINT [FK_DocumentReports_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE, CONSTRAINT [FK_DocumentReports_Documents] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE CASCADE);");
            await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentReports_Status_CreatedAt' AND object_id = OBJECT_ID(N'dbo.DocumentReports')) CREATE INDEX [IX_DocumentReports_Status_CreatedAt] ON [dbo].[DocumentReports] ([Status], [CreatedAt]);");
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.DocumentDownloads', N'U') IS NULL CREATE TABLE [dbo].[DocumentDownloads] ([Id] int IDENTITY(1,1) NOT NULL, [UserId] int NOT NULL, [DocumentId] int NOT NULL, [DownloadedAt] datetime2 NOT NULL, CONSTRAINT [PK_DocumentDownloads] PRIMARY KEY ([Id]), CONSTRAINT [FK_DocumentDownloads_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE, CONSTRAINT [FK_DocumentDownloads_Documents] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents]([Id]) ON DELETE CASCADE);");
            await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentDownloads_UserId_DownloadedAt' AND object_id = OBJECT_ID(N'dbo.DocumentDownloads')) CREATE INDEX [IX_DocumentDownloads_UserId_DownloadedAt] ON [dbo].[DocumentDownloads] ([UserId], [DownloadedAt]);");
            await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentDownloads_DocumentId' AND object_id = OBJECT_ID(N'dbo.DocumentDownloads')) CREATE INDEX [IX_DocumentDownloads_DocumentId] ON [dbo].[DocumentDownloads] ([DocumentId]);");
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.LibraryReferences', N'U') IS NULL CREATE TABLE [dbo].[LibraryReferences] ([Id] int IDENTITY(1,1) NOT NULL, [Type] nvarchar(20) NOT NULL, [Name] nvarchar(120) NOT NULL, [Description] nvarchar(500) NOT NULL, [IsActive] bit NOT NULL, CONSTRAINT [PK_LibraryReferences] PRIMARY KEY ([Id]));");
            await db.Database.ExecuteSqlRawAsync("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LibraryReferences_Type_Name' AND object_id = OBJECT_ID(N'dbo.LibraryReferences')) CREATE UNIQUE INDEX [IX_LibraryReferences_Type_Name] ON [dbo].[LibraryReferences] ([Type], [Name]);");
        }
        else if (db.Database.IsSqlite())
        {
            await EnsureSqlitePublisherColumnAsync(db);
            await EnsureSqliteDocumentApprovalColumnAsync(db);
            await EnsureSqliteDocumentRejectionColumnAsync(db);
            await EnsureSqliteReviewHiddenColumnAsync(db);
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"DocumentReports\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_DocumentReports\" PRIMARY KEY AUTOINCREMENT, \"UserId\" INTEGER NOT NULL, \"DocumentId\" INTEGER NOT NULL, \"Reason\" TEXT NOT NULL, \"Details\" TEXT NOT NULL, \"Status\" TEXT NOT NULL, \"CreatedAt\" TEXT NOT NULL, \"ResolvedAt\" TEXT NULL, \"ResolvedByUserId\" INTEGER NULL, CONSTRAINT \"FK_DocumentReports_Users\" FOREIGN KEY (\"UserId\") REFERENCES \"Users\" (\"Id\") ON DELETE CASCADE, CONSTRAINT \"FK_DocumentReports_Documents\" FOREIGN KEY (\"DocumentId\") REFERENCES \"Documents\" (\"Id\") ON DELETE CASCADE);");
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_DocumentReports_Status_CreatedAt\" ON \"DocumentReports\" (\"Status\", \"CreatedAt\");");
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"DocumentDownloads\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_DocumentDownloads\" PRIMARY KEY AUTOINCREMENT, \"UserId\" INTEGER NOT NULL, \"DocumentId\" INTEGER NOT NULL, \"DownloadedAt\" TEXT NOT NULL, CONSTRAINT \"FK_DocumentDownloads_Users\" FOREIGN KEY (\"UserId\") REFERENCES \"Users\" (\"Id\") ON DELETE CASCADE, CONSTRAINT \"FK_DocumentDownloads_Documents\" FOREIGN KEY (\"DocumentId\") REFERENCES \"Documents\" (\"Id\") ON DELETE CASCADE);");
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_DocumentDownloads_UserId_DownloadedAt\" ON \"DocumentDownloads\" (\"UserId\", \"DownloadedAt\");");
            await db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_DocumentDownloads_DocumentId\" ON \"DocumentDownloads\" (\"DocumentId\");");
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS \"LibraryReferences\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK_LibraryReferences\" PRIMARY KEY AUTOINCREMENT, \"Type\" TEXT NOT NULL, \"Name\" TEXT NOT NULL, \"Description\" TEXT NOT NULL, \"IsActive\" INTEGER NOT NULL);");
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_LibraryReferences_Type_Name\" ON \"LibraryReferences\" (\"Type\", \"Name\");");
            await db.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "AdminAuditLogs" ("Id" INTEGER NOT NULL CONSTRAINT "PK_AdminAuditLogs" PRIMARY KEY AUTOINCREMENT, "ActorEmail" TEXT NOT NULL, "Action" TEXT NOT NULL, "EntityType" TEXT NOT NULL, "EntityId" INTEGER NULL, "Summary" TEXT NOT NULL, "CreatedAt" TEXT NOT NULL);""");
            await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_AdminAuditLogs_CreatedAt" ON "AdminAuditLogs" ("CreatedAt");""");
            await db.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "LibrarySystemSettings" ("Id" INTEGER NOT NULL CONSTRAINT "PK_LibrarySystemSettings" PRIMARY KEY, "RegistrationEnabled" INTEGER NOT NULL, "UpdatedAt" TEXT NOT NULL, "UpdatedBy" TEXT NOT NULL);""");
        }
        if (!await db.SystemSettings.AnyAsync())
        {
            db.SystemSettings.Add(new LibrarySystemSettings());
            await db.SaveChangesAsync();
        }
        if (!await db.Documents.AnyAsync())
        {
            db.Documents.AddRange(
                new(1, "Nhà giả kim", "Paulo Coelho", "Văn học", 2023, "EPUB", "sand", "NH", "Hành trình của Santiago đi tìm kho báu cũng là chuyến đi khám phá tiếng gọi sâu thẳm trong chính mình.", 4.9, 1240),
                new(2, "Tư duy nhanh và chậm", "Daniel Kahneman", "Tâm lý học", 2022, "PDF", "blue", "TD", "Cuốn sách kinh điển giúp chúng ta hiểu hai hệ thống tư duy đang định hình cách đưa ra quyết định.", 4.8, 987),
                new(3, "Tuổi trẻ đáng giá bao nhiêu", "Rosie Nguyễn", "Kỹ năng sống", 2024, "PDF", "coral", "TT", "Những chia sẻ chân thành về học tập, trải nghiệm và cách sống một tuổi trẻ có ý nghĩa.", 4.7, 856),
                new(4, "Đắc nhân tâm", "Dale Carnegie", "Kỹ năng sống", 2021, "EPUB", "green", "ĐN", "Nghệ thuật ứng xử thực tế và tinh tế đã truyền cảm hứng cho hàng triệu độc giả.", 4.8, 792),
                new(5, "Sapiens: Lược sử loài người", "Yuval Noah Harari", "Lịch sử", 2024, "PDF", "violet", "SA", "Một cái nhìn bao quát về hành trình đã đưa Homo sapiens trở thành loài thống trị hành tinh.", 4.9, 728),
                new(6, "Dám bị ghét", "Ichiro Kishimi", "Tâm lý học", 2023, "EPUB", "peach", "DG", "Đối thoại triết học về tự do, trách nhiệm và lòng can đảm để sống cuộc đời của chính mình.", 4.6, 641),
                new(7, "Cà phê cùng Tony", "Tony Buổi Sáng", "Kỹ năng sống", 2022, "PDF", "mint", "CF", "Những câu chuyện dí dỏm về tinh thần tự lập, ham học hỏi và sự tử tế trong cuộc sống.", 4.5, 590),
                new(8, "Đi tìm lẽ sống", "Viktor E. Frankl", "Tâm lý học", 2023, "PDF", "rose", "ĐT", "Từ trải nghiệm sinh tồn, Frankl suy ngẫm về ý nghĩa và sức mạnh tinh thần của con người.", 4.9, 542));
            await db.SaveChangesAsync();
        }

        var documentsMissingPublisher = await db.Documents.Where(d => d.Publisher == "").ToListAsync();
        foreach (var document in documentsMissingPublisher) document.Publisher = "Chưa cập nhật";
        if (documentsMissingPublisher.Count > 0) await db.SaveChangesAsync();
        var referenceValues = await db.Documents.Select(d => new { d.Category, d.Author, d.Publisher }).ToListAsync();
        var references = referenceValues.SelectMany(value => new[]
        {
            new LibraryReference { Type = "Category", Name = value.Category },
            new LibraryReference { Type = "Author", Name = value.Author },
            new LibraryReference { Type = "Publisher", Name = value.Publisher }
        }).GroupBy(reference => new { reference.Type, reference.Name }).Select(group => group.First()).ToList();
        foreach (var reference in references)
            if (!await db.LibraryReferences.AnyAsync(existing => existing.Type == reference.Type && existing.Name == reference.Name))
                db.LibraryReferences.Add(reference);
        await db.SaveChangesAsync();

        var adminEmail = configuration["ONLIBRARY_ADMIN_EMAIL"]?.Trim().ToLowerInvariant();
        var adminPassword = configuration["ONLIBRARY_ADMIN_PASSWORD"];
        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword) && !await db.Users.AnyAsync(u => u.Email == adminEmail))
        {
            var admin = new LibraryUser { Email = adminEmail, DisplayName = "Quản trị viên", Role = "Admin" };
            admin.PasswordHash = hasher.HashPassword(admin, adminPassword);
            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureSqlitePublisherColumnAsync(LibraryDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(\"Documents\")";
            await using var reader = await command.ExecuteReaderAsync();
            var exists = false;
            while (await reader.ReadAsync())
                if (reader.GetString(1).Equals("Publisher", StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
            await reader.CloseAsync();
            if (!exists) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Documents\" ADD COLUMN \"Publisher\" TEXT NOT NULL DEFAULT 'Chưa cập nhật';");
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task EnsureSqliteReviewHiddenColumnAsync(LibraryDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(\"Reviews\")";
            await using var reader = await command.ExecuteReaderAsync();
            var exists = false;
            while (await reader.ReadAsync())
                if (reader.GetString(1).Equals("IsHidden", StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
            await reader.CloseAsync();
            if (!exists) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Reviews\" ADD COLUMN \"IsHidden\" INTEGER NOT NULL DEFAULT 0;");
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task EnsureSqliteDocumentApprovalColumnAsync(LibraryDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(\"Documents\")";
            await using var reader = await command.ExecuteReaderAsync();
            var exists = false;
            while (await reader.ReadAsync())
                if (reader.GetString(1).Equals("IsApproved", StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
            await reader.CloseAsync();
            if (!exists) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Documents\" ADD COLUMN \"IsApproved\" INTEGER NOT NULL DEFAULT 1;");
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task EnsureSqliteDocumentRejectionColumnAsync(LibraryDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            var connection = db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(\"Documents\")";
            await using var reader = await command.ExecuteReaderAsync();
            var exists = false;
            while (await reader.ReadAsync())
                if (reader.GetString(1).Equals("IsRejected", StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
            await reader.CloseAsync();
            if (!exists) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Documents\" ADD COLUMN \"IsRejected\" INTEGER NOT NULL DEFAULT 0;");
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
