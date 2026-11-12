using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ConverterPlus.Models;

public sealed class ConversionItem : INotifyPropertyChanged
{
    private string _targetFormat;
    private string _status = "Готов к конвертации";
    private bool _isBusy;
    private bool _isCompressionMode;

    public ConversionItem(string sourcePath)
    {
        SourcePath = sourcePath;
        SourceExtension = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
        Kind = FormatCatalog.GetKind(SourceExtension);
        TargetFormats = FormatCatalog.GetTargets(Kind, SourceExtension);
        _targetFormat = TargetFormats.FirstOrDefault(format => !format.Equals(SourceExtension, StringComparison.OrdinalIgnoreCase))
            ?? TargetFormats.FirstOrDefault()
            ?? SourceExtension;
        Name = Path.GetFileName(sourcePath);
        Size = FormatCatalog.FormatSize(new FileInfo(sourcePath).Length);
        if (Kind == FileKind.Unsupported)
            _status = "Не поддерживается";
    }

    public string SourcePath { get; }
    public string Name { get; }
    public string SourceExtension { get; }
    public string Size { get; }
    public FileKind Kind { get; }
    public IReadOnlyList<string> TargetFormats { get; }
    public bool IsCompressionMode
    {
        get => _isCompressionMode;
        set
        {
            if (_isCompressionMode == value) return;
            _isCompressionMode = value;
            OnPropertyChanged();
        }
    }

    public string TargetFormat
    {
        get => _targetFormat;
        set
        {
            if (_targetFormat == value) return;
            _targetFormat = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
