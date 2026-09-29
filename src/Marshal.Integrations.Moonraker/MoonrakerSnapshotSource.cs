using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Images;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// Fetches the webcam's <c>snapshot_url</c>, resizes it to JPEG and stores it as
/// <c>&lt;storage&gt;/&lt;printer&gt;/&lt;id&gt;.jpg</c>, keeping the newest <see cref="SnapshotOptions.Retention"/>
/// per printer. The snapshot is served to signed-in users under the link from <see cref="ISnapshotLinks"/>.
/// </summary>
public sealed partial class MoonrakerSnapshotSource(
    IOptions<PrinterOptions> options,
    IPrinterWebcams webcams,
    IHttpClientFactory httpClients,
    IImageResizer resizer,
    ISnapshotLinks links,
    SnapshotStorage storage,
    TimeProvider time) : ISnapshotSource
{
    /// <summary>Largest webcam image accepted.</summary>
    private const int MaxDownloadBytes = 20 * 1024 * 1024;

    public async Task<Result<PrinterSnapshot>> CaptureAsync(string printer, CancellationToken cancellationToken)
    {
        if (Find(printer) is not { } config)
            return PrinterErrors.NotFound(printer);

        Result<PrinterWebcam> webcam = await webcams.FindAsync(config.Name, cancellationToken);
        if (webcam.IsFailure)
            return webcam.Error;

        Result<byte[]> image = await DownloadAsync(config.Name, webcam.Value.SnapshotUrl, cancellationToken);
        if (image.IsFailure)
            return image.Error;

        SnapshotOptions settings = options.Value.PrinterSnapshots;
        Result<ResizedImage> resized = resizer.ToJpeg(image.Value, settings.MaxSize, settings.Quality);
        if (resized.IsFailure)
            return PrinterErrors.SnapshotFailed(config.Name, "the webcam did not send an image.");

        DateTimeOffset now = time.GetUtcNow();
        string id = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture) + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));
        string folder = Folder(config.Name);
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, id + ".jpg"), resized.Value.Jpeg, cancellationToken);
        Prune(folder, settings.Retention);
        return new PrinterSnapshot(config.Name, id, links.For(config.Name, id), resized.Value.Jpeg, resized.Value.Width, resized.Value.Height, now);
    }

    public async Task<Result<byte[]>> OpenAsync(string printer, string id, CancellationToken cancellationToken)
    {
        if (Find(printer) is not { } config)
            return PrinterErrors.NotFound(printer);

        string file = Path.Combine(Folder(config.Name), id + ".jpg");
        if (!SnapshotId().IsMatch(id) || !File.Exists(file))
            return PrinterErrors.SnapshotNotFound(id);

        return await File.ReadAllBytesAsync(file, cancellationToken);
    }

    private async Task<Result<byte[]>> DownloadAsync(string printer, Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await httpClients.CreateClient(MoonrakerRest.HttpClientName).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return PrinterErrors.SnapshotFailed(printer, $"the webcam answered HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaxDownloadBytes)
                return PrinterErrors.SnapshotFailed(printer, "the image is too large.");

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxDownloadBytes)
                    return PrinterErrors.SnapshotFailed(printer, "the image is too large.");
                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (HttpRequestException exception)
        {
            return PrinterErrors.SnapshotFailed(printer, exception.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PrinterErrors.SnapshotFailed(printer, "the webcam did not answer in time.");
        }
    }

    private static void Prune(string folder, int retention)
    {
        string[] files = [.. Directory.GetFiles(folder, "*.jpg").OrderDescending(StringComparer.Ordinal)];
        foreach (string old in files.Skip(Math.Max(1, retention)))
            File.Delete(old);
    }

    private PrinterConfig? Find(string printer) =>
        options.Value.Printers.FirstOrDefault(config => config.Url is not null && string.Equals(config.Name, printer, StringComparison.OrdinalIgnoreCase));

    private string Folder(string printer) =>
        Path.Combine(storage.Directory, SafeName().IsMatch(printer) ? printer : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(printer)))[..16]);

    [GeneratedRegex("^[0-9]{8}T[0-9]{9}-[0-9a-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex SnapshotId();

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
