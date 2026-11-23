namespace OnLibrary.Services;

public interface IFileScanner
{
    Task ScanAsync(string path, CancellationToken cancellationToken = default);
}
