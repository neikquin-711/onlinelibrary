using System.Diagnostics;

namespace OnLibrary.Services;

public sealed class ClamAvFileScanner(IConfiguration configuration) : IFileScanner
{
    public async Task ScanAsync(string path, CancellationToken cancellationToken = default)
    {
        var executable = configuration["ONLIBRARY_CLAMSCAN_PATH"];
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("Chưa cấu hình ClamAV. Hãy đặt ONLIBRARY_CLAMSCAN_PATH để bật upload an toàn.");

        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--no-summary");
        start.ArgumentList.Add(path);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Không thể khởi chạy trình quét virus.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(40));
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode == 1) throw new InvalidDataException("Tệp bị phần mềm diệt virus đánh dấu là không an toàn.");
            if (process.ExitCode != 0) throw new InvalidOperationException($"ClamAV không hoàn tất việc quét tệp: {error} {output}".Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Quét virus quá thời gian cho phép.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("Không tìm thấy ClamAV. Kiểm tra ONLIBRARY_CLAMSCAN_PATH.", ex);
        }
    }
}
