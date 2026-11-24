using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnLibrary.Data;
using OnLibrary.Models;

namespace OnLibrary.Controllers;

public class AccountController(LibraryDbContext db, IPasswordHasher<LibraryUser> passwordHasher) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null) { ViewBag.ReturnUrl = returnUrl; return View(); }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string? email, string? password, string? returnUrl)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        var user = normalizedEmail is null ? null : await db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "") == PasswordVerificationResult.Failed)
        {
            ViewBag.Error = "Email hoặc mật khẩu chưa chính xác.";
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }
        await SignIn(user, rememberMe: false);
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(string? displayName, string? email, string? password)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        var valid = !string.IsNullOrWhiteSpace(displayName) && displayName.Trim().Length <= 80 &&
            normalizedEmail is not null && normalizedEmail.Length <= 254 &&
            Regex.IsMatch(normalizedEmail, @"^[^\s@]+@[^\s@]+\.[^\s@]+$") &&
            password is { Length: >= 10 and <= 128 } && password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit);
        if (!valid)
        {
            ViewBag.Error = "Hãy nhập tên, email hợp lệ và mật khẩu từ 10 ký tự gồm chữ hoa, chữ thường và số.";
            return View();
        }
        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail))
        {
            ViewBag.Error = "Email này đã được đăng ký.";
            return View();
        }
        var user = new LibraryUser { Email = normalizedEmail!, DisplayName = displayName!.Trim(), Role = "Reader" };
        user.PasswordHash = passwordHasher.HashPassword(user, password!);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            ViewBag.Error = "Không thể tạo tài khoản lúc này. Hãy thử lại.";
            return View();
        }
        await SignIn(user, rememberMe: false);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    public IActionResult AccessDenied() => View();

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Challenge();
        var user = await db.Users.FindAsync(userId);
        return user is null ? NotFound() : View(user);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(string? displayName, string? email, string? currentPassword,
        string? newPassword, string? confirmPassword)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Challenge();
        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var normalizedEmail = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 80 || normalizedEmail is null ||
            normalizedEmail.Length > 254 || !Regex.IsMatch(normalizedEmail, @"^[^\s@]+@[^\s@]+\.[^\s@]+$") ||
            string.IsNullOrWhiteSpace(currentPassword) ||
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            ViewBag.Error = "Hãy nhập tên, email hợp lệ và mật khẩu hiện tại chính xác.";
            return View(user);
        }
        if (await db.Users.AnyAsync(u => u.Id != userId && u.Email == normalizedEmail))
        {
            ViewBag.Error = "Email này đã được tài khoản khác sử dụng.";
            return View(user);
        }
        if (!string.IsNullOrEmpty(newPassword))
        {
            if (newPassword.Length is < 10 or > 128 || !newPassword.Any(char.IsUpper) ||
                !newPassword.Any(char.IsLower) || !newPassword.Any(char.IsDigit) || newPassword != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu mới phải khớp xác nhận, dài 10–128 ký tự và có chữ hoa, chữ thường, chữ số.";
                return View(user);
            }
            user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        }

        user.DisplayName = displayName.Trim();
        user.Email = normalizedEmail;
        await db.SaveChangesAsync();
        await SignIn(user, rememberMe: false);
        TempData["ProfileSaved"] = "Thông tin hồ sơ đã được cập nhật.";
        return RedirectToAction(nameof(Profile));
    }

    private async Task SignIn(LibraryUser user, bool rememberMe)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = rememberMe });
    }
}
