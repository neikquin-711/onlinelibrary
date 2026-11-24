namespace OnLibrary.Models;

public sealed class LibrarySystemSettings
{
    public int Id { get; set; } = 1;
    public bool RegistrationEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = "system";
}
