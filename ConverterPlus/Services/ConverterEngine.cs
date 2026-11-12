using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ConverterPlus.Models;
using ImageMagick;

namespace ConverterPlus;

internal static class ConverterEngine
{
    private static readonly SemaphoreSlim NvencProbeGate = new(1, 1);
    private static bool? _nvencAvailable;

    public static int ImageQuality { get; set; } = 90;
    public static bool CompressionMode { get; set; }

    public static bool HasFfmpeg => FindFfmpeg() is not null;
    public static bool HasLibreOffice => FindLibreOffice() is not null;

    public static async Task<long> ConvertAsync(ConversionItem item, string outputPath, CancellationToken cancellationToken, long? targetBytes = null)
    {
        var extension = item.TargetFormat.ToLowerInvariant();
        var tempPath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Guid.NewGuid():N}.{extension}");
        try
        {
            var sameFormat = item.SourceExtension.Equals(extension, StringComparison.OrdinalIgnoreCase);
            var shouldCompress = CompressionMode && item.Kind is (FileKind.Image or FileKind.Audio or FileKind.Video);
            var sourceWithinLimit = targetBytes.HasValue && new FileInfo(item.SourcePath).Length <= targetBytes.Value;
            if (sameFormat && (!shouldCompress || sourceWithinLimit))
            {
                File.Copy(item.SourcePath, tempPath);
            }
            else
            {
                switch (item.Kind)
                {
                    case FileKind.Image:
                        if (CompressionMode && extension == "gif" && item.SourceExtension == "gif")
                            await ConvertMediaAsync(item, tempPath, cancellationToken, targetBytes);
                        else
                            await Task.Run(() => ConvertImage(item.SourcePath, tempPath), cancellationToken);
                        break;
                    case FileKind.Audio:
                        await ConvertMediaAsync(item, tempPath, cancellationToken, targetBytes);
                        break;
                    case FileKind.Video:
                        if (targetBytes.HasValue)
                            await ConvertVideoToTargetAsync(item, tempPath, targetBytes.Value, cancellationToken);
                        else
                            await ConvertMediaAsync(item, tempPath, cancellationToken);
                        break;
                    case FileKind.TextDocument:
                    case FileKind.Spreadsheet:
                    case FileKind.Presentation:
                    case FileKind.Pdf:
                        await ConvertOfficeAsync(item.SourcePath, item.TargetFormat, tempPath, cancellationToken);
                        break;
                    default:
                        throw new NotSupportedException("Этот тип файла пока не поддерживается.");
                }
            }

            if (targetBytes.HasValue && item.Kind == FileKind.Image && extension is ("jpg" or "jpeg" or "webp" or "avif"))
                await ReduceImageToTargetAsync(item.SourcePath, tempPath, targetBytes.Value, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, outputPath);
            return new FileInfo(outputPath).Length;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static async Task ReduceImageToTargetAsync(string sourcePath, string tempPath, long targetBytes, CancellationToken cancellationToken)
    {
        var previousQuality = ImageQuality;
        try
        {
            var quality = Math.Clamp(previousQuality, 60, 100);
            for (var attempt = 0; attempt < 5 && new FileInfo(tempPath).Length > targetBytes && quality > 60; attempt++)
            {
                var currentSize = new FileInfo(tempPath).Length;
                var reduction = Math.Max(5, (int)Math.Ceiling((1d - (double)targetBytes / currentSize) * 30));
                quality = Math.Max(60, quality - reduction);
                ImageQuality = quality;
                TryDelete(tempPath);
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Run(() => ConvertImage(sourcePath, tempPath), cancellationToken);
            }
        }
        finally
        {
            ImageQuality = previousQuality;
        }
    }

    private static void ConvertImage(string sourcePath, string outputPath)
    {
        using var image = new MagickImage(sourcePath);
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (extension is ".jpg" or ".jpeg" or ".webp" or ".avif" or ".heic" or ".tif" or ".tiff")
            image.Quality = (uint)ImageQuality;
        else if (extension == ".png")
        {
            var compressionLevel = (int)Math.Round((100 - ImageQuality) * 9d / 40d);
            image.Settings.SetDefine("png:compression-level", compressionLevel.ToString(CultureInfo.InvariantCulture));
        }
        image.Write(outputPath);
    }

    private static async Task ConvertMediaAsync(ConversionItem item, string outputPath, CancellationToken cancellationToken, long? targetBytes = null)
        => await ConvertMediaAsync(item, outputPath, cancellationToken, targetBytes, null);

    private static async Task ConvertMediaAsync(
        ConversionItem item,
        string outputPath,
        CancellationToken cancellationToken,
        long? targetBytes,
        double? durationOverride)
    {
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException("Для аудио и видео нужен FFmpeg. Переустановите приложение, чтобы восстановить его.");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", item.SourcePath };
        var target = item.TargetFormat.ToLowerInvariant();
        var quality = Math.Clamp(ImageQuality, 60, 100);
        var audioBitrate = quality <= 70 ? "96k" : quality <= 85 ? "160k" : "256k";
        var audioQuality = quality <= 70 ? "2" : quality <= 85 ? "4" : "6";
        var videoQuality = quality <= 70 ? "7" : quality <= 85 ? "5" : "3";
        var duration = targetBytes.HasValue
            ? durationOverride ?? await ProbeDurationAsync(ffmpeg, item.SourcePath, cancellationToken)
            : null;
        long? totalBitrate = duration is > 0
            ? (long)(targetBytes!.Value * 8d * 0.9d / duration.Value)
            : null;
        var targetAudioBitrate = totalBitrate.HasValue
            ? Math.Clamp((long)(item.Kind == FileKind.Video ? Math.Min(128_000, totalBitrate.Value * 0.12) : totalBitrate.Value), 32_000, 512_000)
            : (long?)null;
        var targetVideoBitrate = totalBitrate.HasValue
            ? Math.Max(64_000, totalBitrate.Value - (item.Kind == FileKind.Video ? targetAudioBitrate ?? 0 : 0))
            : (long?)null;
        var chosenAudioBitrate = targetAudioBitrate.HasValue ? FormatBitrate(targetAudioBitrate.Value) : audioBitrate;
        var chosenVideoBitrate = targetVideoBitrate.HasValue ? FormatBitrate(targetVideoBitrate.Value) : null;
        var useNvenc = item.Kind == FileKind.Video
            && target is ("mp4" or "mkv" or "mov")
            && await IsNvencAvailableAsync(ffmpeg, cancellationToken);

        if (item.Kind == FileKind.Audio || target is "mp3" or "wav")
        {
            args.AddRange(["-vn"]);
            switch (target)
            {
                case "mp3": args.AddRange(["-c:a", "libmp3lame", "-b:a", chosenAudioBitrate]); break;
                case "wav": args.AddRange(["-c:a", "pcm_s16le"]); break;
                case "flac": args.AddRange(["-c:a", "flac"]); break;
                case "aac": args.AddRange(["-c:a", "aac", "-b:a", chosenAudioBitrate]); break;
                case "m4a": args.AddRange(["-c:a", "aac", "-b:a", chosenAudioBitrate]); break;
                case "ogg":
                    args.AddRange(targetAudioBitrate.HasValue
                        ? ["-c:a", "libvorbis", "-b:a", chosenAudioBitrate]
                        : ["-c:a", "libvorbis", "-q:a", audioQuality]);
                    break;
                case "opus": args.AddRange(["-c:a", "libopus", "-b:a", chosenAudioBitrate]); break;
                default: throw new NotSupportedException($"Формат аудио {target} не поддерживается.");
            }
        }
        else
        {
            switch (target)
            {
                case "mp4":
                case "mkv":
                case "mov":
                    if (useNvenc)
                    {
                        if (chosenVideoBitrate is not null)
                            args.AddRange(["-c:v", "h264_nvenc", "-preset", "p4", "-b:v", chosenVideoBitrate, "-maxrate", chosenVideoBitrate, "-bufsize", chosenVideoBitrate]);
                        else
                        {
                            var cq = (int)Math.Round(42 - (quality - 60) * 0.6);
                            args.AddRange(["-c:v", "h264_nvenc", "-preset", "p4", "-rc", "vbr", "-cq", cq.ToString(CultureInfo.InvariantCulture), "-b:v", "0"]);
                        }
                    }
                    else
                    {
                        args.AddRange(chosenVideoBitrate is not null
                            ? ["-c:v", "mpeg4", "-b:v", chosenVideoBitrate]
                            : ["-c:v", "mpeg4", "-q:v", videoQuality]);
                    }
                    args.AddRange(["-c:a", "aac", "-b:a", chosenAudioBitrate]);
                    if (target == "mp4") args.AddRange(["-movflags", "+faststart"]);
                    break;
                case "avi":
                    args.AddRange(chosenVideoBitrate is not null
                        ? ["-c:v", "mpeg4", "-b:v", chosenVideoBitrate, "-c:a", "libmp3lame", "-b:a", chosenAudioBitrate]
                        : ["-c:v", "mpeg4", "-q:v", videoQuality, "-c:a", "libmp3lame", "-b:a", chosenAudioBitrate]);
                    break;
                case "webm":
                    var crf = quality <= 70 ? "38" : quality <= 85 ? "30" : "22";
                    args.AddRange(chosenVideoBitrate is not null
                        ? ["-c:v", "libvpx-vp9", "-deadline", "good", "-cpu-used", "4", "-b:v", chosenVideoBitrate,
                            "-c:a", "libopus", "-b:a", chosenAudioBitrate]
                        : ["-c:v", "libvpx-vp9", "-deadline", "good", "-cpu-used", "4", "-crf", crf,
                            "-b:v", "0", "-c:a", "libopus", "-b:a", chosenAudioBitrate]);
                    break;
                case "gif":
                    var fps = quality <= 70 ? 8 : quality <= 85 ? 12 : 18;
                    var scale = quality <= 70 ? 540 : quality <= 85 ? 720 : 960;
                    args.AddRange(["-vf", $"fps={fps},scale={scale}:-1:flags=lanczos", "-loop", "0"]);
                    break;
                default: throw new NotSupportedException($"Формат видео {target} не поддерживается.");
            }
        }

        args.Add(outputPath);
        await RunProcessAsync(ffmpeg, args, cancellationToken);
    }

    private static async Task ConvertVideoToTargetAsync(
        ConversionItem item,
        string outputPath,
        long targetBytes,
        CancellationToken cancellationToken)
    {
        var ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException("Для аудио и видео нужен FFmpeg. Переустановите приложение, чтобы восстановить его.");
        var duration = await ProbeDurationAsync(ffmpeg, item.SourcePath, cancellationToken);
        if (duration is not > 0)
            throw new InvalidOperationException("Не удалось определить длительность видео, поэтому сжатие до заданного размера отменено.");

        var bitrateTargetBytes = targetBytes;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            TryDelete(outputPath);
            await ConvertMediaAsync(item, outputPath, cancellationToken, bitrateTargetBytes, duration);
            var outputBytes = new FileInfo(outputPath).Length;
            if (outputBytes <= targetBytes)
                return;

            if (attempt == 5)
                break;

            var correction = Math.Clamp((double)targetBytes / outputBytes * 0.88, 0.2, 0.85);
            var nextTarget = (long)(bitrateTargetBytes * correction);
            if (nextTarget >= bitrateTargetBytes)
                break;
            bitrateTargetBytes = Math.Max(1, nextTarget);
        }

        TryDelete(outputPath);
        throw new InvalidOperationException("Видео не удалось сжать до заданного размера после нескольких попыток.");
    }

    private static async Task<bool> IsNvencAvailableAsync(string ffmpeg, CancellationToken cancellationToken)
    {
        await NvencProbeGate.WaitAsync(cancellationToken);
        try
        {
            if (_nvencAvailable.HasValue)
                return _nvencAvailable.Value;

            var probe = new[]
            {
                "-hide_banner", "-loglevel", "error", "-nostdin", "-f", "lavfi", "-i",
                "color=c=black:s=32x32:d=0.04", "-frames:v", "1", "-an", "-c:v", "h264_nvenc",
                "-preset", "p4", "-f", "null", "-"
            };
            try
            {
                await RunProcessAsync(ffmpeg, probe, cancellationToken);
                _nvencAvailable = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                _nvencAvailable = false;
            }

            return _nvencAvailable.Value;
        }
        finally
        {
            NvencProbeGate.Release();
        }
    }

    private static async Task<double?> ProbeDurationAsync(string ffmpeg, string sourcePath, CancellationToken cancellationToken)
    {
        var ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (!File.Exists(ffprobe))
            ffprobe = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory, "ffprobe.exe"))
                .FirstOrDefault(File.Exists) ?? string.Empty;
        if (string.IsNullOrEmpty(ffprobe))
            return null;

        var startInfo = new ProcessStartInfo(ffprobe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in new[]
        {
            "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", sourcePath
        })
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                return null;
            var output = await outputTask;
            return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
                && duration > 0
                ? duration
                : null;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBitrate(long bitsPerSecond) =>
        $"{Math.Max(32, (long)Math.Round(bitsPerSecond / 1000d))}k";

    private static async Task ConvertOfficeAsync(string sourcePath, string targetFormat, string outputPath, CancellationToken cancellationToken)
    {
        var soffice = FindLibreOffice() ?? throw new InvalidOperationException("Для документов установите бесплатный LibreOffice. Кнопка загрузки находится над списком файлов.");
        var staging = Path.Combine(Path.GetTempPath(), "ConverterPlus", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var args = new List<string> { "--headless", "--nologo", "--nodefault", "--nolockcheck", "--convert-to", targetFormat, "--outdir", staging, sourcePath };
            await RunProcessAsync(soffice, args, cancellationToken);
            var generated = Path.Combine(staging, $"{Path.GetFileNameWithoutExtension(sourcePath)}.{targetFormat}");
            if (!File.Exists(generated))
                throw new InvalidOperationException("LibreOffice не создал файл. Возможно, этот формат не подходит для выбранного документа.");
            File.Move(generated, outputPath);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    private static async Task RunProcessAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null && errors.Length < 3000)
                errors.AppendLine(eventArgs.Data);
        };

        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw;
        }

        if (process.ExitCode != 0)
        {
            var detail = errors.ToString().Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? "Внешний конвертер завершился с ошибкой."
                : detail.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Last());
        }
    }

    private static string? FindFfmpeg()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
        if (File.Exists(bundled)) return bundled;
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "ffmpeg.exe"))
            .FirstOrDefault(File.Exists);
        return path;
    }

    private static string? FindLibreOffice()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "LibreOffice", "program", "soffice.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe")
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
                .Select(directory => Path.Combine(directory, "soffice.exe"))
                .FirstOrDefault(File.Exists);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
