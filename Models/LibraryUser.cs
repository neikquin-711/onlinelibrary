namespace OnLibrary.Models;

public sealed class LibraryUser
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Reader";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
