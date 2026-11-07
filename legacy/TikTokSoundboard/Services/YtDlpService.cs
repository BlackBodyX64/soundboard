using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace TikTokSoundboard.Services;

public class DownloadProgressInfo
{
    public double Percentage { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// Downloads audio from YouTube / TikTok URLs using yt-dlp.
/// yt-dlp binary is auto-downloaded on first use.
/// </summary>
public class YtDlpService
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TikTokSoundboard");

    private static readonly string ToolsDir = Path.Combine(AppDir, "tools");
    private static readonly string YtDlpPath = Path.Combine(ToolsDir, "yt-dlp.exe");
    private static readonly string FfmpegPath = Path.Combine(ToolsDir, "ffmpeg.exe");

    private const string YtDlpDownloadUrl =
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    private const string FfmpegDownloadUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    /// <summary>
    /// Check if a URL looks like a supported platform link.
    /// </summary>
    public static bool IsSupportedUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        url = url.Trim().ToLowerInvariant();
        return url.Contains("youtube.com") ||
               url.Contains("youtu.be") ||
               url.Contains("tiktok.com") ||
               url.Contains("vm.tiktok.com");
    }

    /// <summary>
    /// Ensure yt-dlp.exe exists locally. Downloads it if not.
    /// </summary>
    public async Task EnsureYtDlpAsync(IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(ToolsDir);

        if (!File.Exists(YtDlpPath))
        {
            progress?.Report(new DownloadProgressInfo { Message = "กำลังดาวน์โหลด yt-dlp...", Percentage = 0 });
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TikTokSoundboard/1.0");
            var bytes = await http.GetByteArrayAsync(YtDlpDownloadUrl, ct);
            await File.WriteAllBytesAsync(YtDlpPath, bytes, ct);
            progress?.Report(new DownloadProgressInfo { Message = "ดาวน์โหลด yt-dlp สำเร็จ", Percentage = 100 });
        }
    }

    /// <summary>
    /// Ensure ffmpeg exists locally. Downloads it if not.
    /// </summary>
    public async Task EnsureFfmpegAsync(IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(ToolsDir);

        if (!File.Exists(FfmpegPath))
        {
            progress?.Report(new DownloadProgressInfo { Message = "กำลังดาวน์โหลด ffmpeg (อาจใช้เวลาสักครู่)...", Percentage = 0 });
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TikTokSoundboard/1.0");

            var zipPath = Path.Combine(ToolsDir, "ffmpeg.zip");
            var extractDir = Path.Combine(ToolsDir, "ffmpeg_extract");

            // Download zip
            using (var stream = await http.GetStreamAsync(FfmpegDownloadUrl, ct))
            using (var fs = File.Create(zipPath))
            {
                await stream.CopyToAsync(fs, ct);
            }

            progress?.Report(new DownloadProgressInfo { Message = "กำลังแตกไฟล์ ffmpeg...", Percentage = 50 });

            // Extract
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);

            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // Find ffmpeg.exe inside extracted directory
            var ffmpegExe = Directory.GetFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (ffmpegExe != null)
            {
                File.Copy(ffmpegExe, FfmpegPath, true);

                // Also copy ffprobe if available
                var ffprobeExe = Directory.GetFiles(extractDir, "ffprobe.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (ffprobeExe != null)
                {
                    File.Copy(ffprobeExe, Path.Combine(ToolsDir, "ffprobe.exe"), true);
                }
            }

            // Cleanup
            try
            {
                File.Delete(zipPath);
                Directory.Delete(extractDir, true);
            }
            catch { }

            progress?.Report(new DownloadProgressInfo { Message = "ดาวน์โหลด ffmpeg สำเร็จ", Percentage = 100 });
        }
    }

    /// <summary>
    /// Get the title of a video from a URL.
    /// </summary>
    public async Task<string?> GetTitleAsync(string url, CancellationToken ct = default)
    {
        await EnsureYtDlpAsync(ct: ct);

        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            Arguments = $"--get-title \"{url}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var proc = Process.Start(psi);
        if (proc == null) return null;

        var title = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return title?.Trim();
    }

    /// <summary>
    /// Download audio from a YouTube or TikTok URL.
    /// Returns the path to the downloaded audio file.
    /// </summary>
    public async Task<string?> DownloadAudioAsync(string url, string downloadDir,
        string audioFormat = "mp3",
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken ct = default)
    {
        // Ensure tools
        await EnsureYtDlpAsync(progress, ct);
        await EnsureFfmpegAsync(progress, ct);

        Directory.CreateDirectory(downloadDir);

        progress?.Report(new DownloadProgressInfo { Message = "กำลังเริ่มดาวน์โหลดเสียง...", Percentage = 0 });

        // Generate safe filename using output template
        var outputTemplate = Path.Combine(downloadDir, "%(title)s.%(ext)s");

        var args = $"--no-playlist " +
                   $"--extract-audio " +
                   $"--audio-format {audioFormat} " +
                   $"--audio-quality 0 " +
                   $"--ffmpeg-location \"{ToolsDir}\" " +
                   $"-o \"{outputTemplate}\" " +
                   $"--no-overwrites " +
                   $"\"{url}\"";

        var psi = new ProcessStartInfo
        {
            FileName = YtDlpPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = downloadDir,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            progress?.Report(new DownloadProgressInfo { Message = "ไม่สามารถเริ่มกระบวนการดาวน์โหลดได้", Percentage = 0 });
            return null;
        }

        string? outputFilePath = null;
        var destinationRegex = new Regex(@"\[ExtractAudio\] Destination:\s*(.+)$",
            RegexOptions.Multiline);
        var alreadyRegex = new Regex($@"\[download\]\s+(.+\.{audioFormat})\s+has already been downloaded",
            RegexOptions.Multiline);
        var mergerRegex = new Regex(@"\[Merger\] Merging formats into\s+""?(.+?)""?\s*$",
            RegexOptions.Multiline);

        // Read output in background
        var outputBuilder = new System.Text.StringBuilder();

        _ = Task.Run(async () =>
        {
            while (!proc.StandardOutput.EndOfStream)
            {
                var line = await proc.StandardOutput.ReadLineAsync(ct);
                if (line != null)
                {
                    outputBuilder.AppendLine(line);
                    Debug.WriteLine($"[yt-dlp stdout] {line}");

                    // Parse progress: [download] 10.0% of ~5.00MiB at 1.25MiB/s ETA 00:03
                    if (line.Contains("[download]") && line.Contains("%"))
                    {
                        var matchSize = Regex.Match(line, @"(\d+\.?\d*)%\s+of\s+~?\s*(\d+\.?\d*[a-zA-Z]+)");
                        if (matchSize.Success)
                        {
                            if (double.TryParse(matchSize.Groups[1].Value, out double pct))
                            {
                                progress?.Report(new DownloadProgressInfo 
                                { 
                                    Percentage = pct, 
                                    Message = $"กำลังดาวน์โหลด... {pct}% (ขนาด {matchSize.Groups[2].Value})" 
                                });
                            }
                        }
                        else
                        {
                            var match = Regex.Match(line, @"(\d+\.?\d*)%");
                            if (match.Success && double.TryParse(match.Groups[1].Value, out double pct))
                            {
                                progress?.Report(new DownloadProgressInfo 
                                { 
                                    Percentage = pct, 
                                    Message = $"กำลังดาวน์โหลด... {pct}%" 
                                });
                            }
                        }
                    }
                }
            }
        }, ct);

        var errorBuilder = new System.Text.StringBuilder();
        _ = Task.Run(async () =>
        {
            while (!proc.StandardError.EndOfStream)
            {
                var line = await proc.StandardError.ReadLineAsync(ct);
                if (line != null)
                {
                    errorBuilder.AppendLine(line);
                    Debug.WriteLine($"[yt-dlp stderr] {line}");
                }
            }
        }, ct);

        await proc.WaitForExitAsync(ct);

        var fullOutput = outputBuilder.ToString() + errorBuilder.ToString();

        // Try to find output file from yt-dlp output
        var destMatch = destinationRegex.Match(fullOutput);
        if (destMatch.Success)
        {
            outputFilePath = destMatch.Groups[1].Value.Trim().Trim('"');
        }
        else
        {
            var alreadyMatch = alreadyRegex.Match(fullOutput);
            if (alreadyMatch.Success)
            {
                outputFilePath = alreadyMatch.Groups[1].Value.Trim();
            }
        }

        // Fallback: look for the newest file of the correct format in downloads dir
        if (string.IsNullOrEmpty(outputFilePath) || !File.Exists(outputFilePath))
        {
            outputFilePath = Directory.GetFiles(downloadDir, $"*.{audioFormat}")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
        }

        if (proc.ExitCode != 0 && string.IsNullOrEmpty(outputFilePath))
        {
            var error = errorBuilder.ToString();
            progress?.Report(new DownloadProgressInfo { Message = $"ดาวน์โหลดไม่สำเร็จ: {error}", Percentage = 0 });
            return null;
        }

        if (!string.IsNullOrEmpty(outputFilePath) && File.Exists(outputFilePath))
        {
            progress?.Report(new DownloadProgressInfo { Message = "ดาวน์โหลดสำเร็จ! ✓", Percentage = 100 });
            return outputFilePath;
        }

        progress?.Report(new DownloadProgressInfo { Message = "ไม่พบไฟล์ที่ดาวน์โหลด", Percentage = 0 });
        return null;
    }
}
