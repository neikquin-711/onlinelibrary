using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OnLibrary.Data;
using OnLibrary.Models;
using OnLibrary.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddControllersWithViews();
var keyDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "data-protection-keys-v2");
Directory.CreateDirectory(keyDirectory);
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("OnLibrary");
if (OperatingSystem.IsWindows() && builder.Configuration.GetValue<bool>("ONLIBRARY_PROTECT_KEYS_WITH_DPAPI"))
    dataProtection.ProtectKeysWithDpapi();
var sqlServerConnection = builder.Configuration["ONLIBRARY_DB"];
var sqlitePath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "onlibrary.db");
Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath)!);
builder.Services.AddDbContext<LibraryDbContext>(options =>
{
    if (string.IsNullOrWhiteSpace(sqlServerConnection)) options.UseSqlite($"Data Source={sqlitePath}");
    else options.UseSqlServer(sqlServerConnection);
});
builder.Services.AddScoped<IPasswordHasher<LibraryUser>, PasswordHasher<LibraryUser>>();
builder.Services.AddSingleton<IFileScanner, ClamAvFileScanner>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "OnLibrary.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});
builder.Services.AddAuthorization();
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<LibraryUser>>();
    await LibraryDataSeeder.InitializeAsync(db, hasher, app.Configuration);
}
app.Run();
