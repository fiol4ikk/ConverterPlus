namespace ConverterPlus.Models;

public enum FileKind
{
    Image,
    Audio,
    Video,
    TextDocument,
    Spreadsheet,
    Presentation,
    Pdf,
    Unsupported
}

public static class FormatCatalog
{
    private static readonly string[] ImageFormats = ["png", "jpg", "webp", "bmp", "tif", "gif", "ico", "avif"];
    private static readonly string[] AudioFormats = ["mp3", "wav", "flac", "aac", "m4a", "ogg", "opus"];
    private static readonly string[] VideoFormats = ["mp4", "mkv", "mov", "webm", "avi", "gif"];
    private static readonly string[] TextDocumentFormats = ["docx", "odt", "rtf", "txt", "html", "pdf"];
    private static readonly string[] SpreadsheetFormats = ["xlsx", "ods", "csv", "pdf"];
    private static readonly string[] PresentationFormats = ["pptx", "odp", "pdf"];

    public static FileKind GetKind(string extension) => extension.ToLowerInvariant() switch
    {
        "png" or "jpg" or "jpeg" or "webp" or "bmp" or "tif" or "tiff" or "gif" or "ico" or "avif" or "heic" => FileKind.Image,
        "mp3" or "wav" or "flac" or "aac" or "m4a" or "ogg" or "opus" or "wma" => FileKind.Audio,
        "mp4" or "mkv" or "mov" or "webm" or "avi" or "wmv" or "mpeg" or "mpg" => FileKind.Video,
        "doc" or "docx" or "odt" or "rtf" or "txt" or "html" or "htm" => FileKind.TextDocument,
        "xls" or "xlsx" or "ods" or "csv" => FileKind.Spreadsheet,
        "ppt" or "pptx" or "odp" => FileKind.Presentation,
        "pdf" => FileKind.Pdf,
        _ => FileKind.Unsupported
    };

    public static IReadOnlyList<string> GetTargets(FileKind kind, string sourceExtension) => kind switch
    {
        FileKind.Image => ImageFormats,
        FileKind.Audio => AudioFormats,
        FileKind.Video => VideoFormats.Concat(["mp3", "wav", "gif"]).Distinct().ToArray(),
        FileKind.TextDocument => TextDocumentFormats,
        FileKind.Spreadsheet => SpreadsheetFormats,
        FileKind.Presentation => PresentationFormats,
        FileKind.Pdf => ["docx", "odt"],
        _ => [sourceExtension]
    };

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} КБ",
        < 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024):0.#} МБ",
        _ => $"{bytes / (1024d * 1024 * 1024):0.##} ГБ"
    };
}
