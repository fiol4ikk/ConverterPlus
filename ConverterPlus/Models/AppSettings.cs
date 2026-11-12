using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ConverterPlus.Models;

public sealed class AppSettings : INotifyPropertyChanged
{
    private string _themeMode = "System";
    private string _material = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? "Acrylic" : "Mica";
    private string _accentColor = "Violet";
    private string _language = "Auto";
    private int _imageQuality = 90;
    private bool _rememberLastFolder = true;
    private bool _openOutputFolderAfterConversion;
    private bool _hasCompletedOnboarding;
    private int _onboardingVersion;
    private string? _lastOutputFolder;
    private string? _lastConversionOutputFolder;
    private string? _lastCompressionOutputFolder;

    public string ThemeMode { get => _themeMode; set => Set(ref _themeMode, value); }
    public string Material { get => _material; set => Set(ref _material, value); }
    public string AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }
    public string Language { get => _language; set => Set(ref _language, value); }
    public int ImageQuality { get => _imageQuality; set => Set(ref _imageQuality, Math.Clamp(value, 60, 100)); }
    public bool RememberLastFolder { get => _rememberLastFolder; set => Set(ref _rememberLastFolder, value); }
    public bool OpenOutputFolderAfterConversion { get => _openOutputFolderAfterConversion; set => Set(ref _openOutputFolderAfterConversion, value); }
    public bool HasCompletedOnboarding { get => _hasCompletedOnboarding; set => Set(ref _hasCompletedOnboarding, value); }
    public int OnboardingVersion { get => _onboardingVersion; set => Set(ref _onboardingVersion, value); }
    public string? LastOutputFolder { get => _lastOutputFolder; set => Set(ref _lastOutputFolder, value); }
    public string? LastConversionOutputFolder { get => _lastConversionOutputFolder; set => Set(ref _lastConversionOutputFolder, value); }
    public string? LastCompressionOutputFolder { get => _lastCompressionOutputFolder; set => Set(ref _lastCompressionOutputFolder, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSettings Clone() => new()
    {
        ThemeMode = ThemeMode,
        Material = Material,
        AccentColor = AccentColor,
        Language = Language,
        ImageQuality = ImageQuality,
        RememberLastFolder = RememberLastFolder,
        OpenOutputFolderAfterConversion = OpenOutputFolderAfterConversion,
        HasCompletedOnboarding = HasCompletedOnboarding,
        OnboardingVersion = OnboardingVersion,
        LastOutputFolder = LastOutputFolder,
        LastConversionOutputFolder = LastConversionOutputFolder,
        LastCompressionOutputFolder = LastCompressionOutputFolder
    };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
