using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ConverterPlus.Models;
using ConverterPlus.Services;
using Microsoft.Win32;

namespace ConverterPlus;

public partial class MainWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const double ResizeGrip = 4;

    public ObservableCollection<ConversionItem> Files { get; } = [];

    private CancellationTokenSource? _conversionCts;
    private bool _isConverting;
    private bool _queueWasEmpty = true;
    private bool _compressionMode;
    private bool _updatingQualityText;
    private bool _qualityDirty;
    private bool _isInitializing = true;
    private bool _isReinstallWelcome;
    private string _sizeUnit = "MB";
    private string _conversionOutputFolder = string.Empty;
    private string _compressionOutputFolder = string.Empty;
    private readonly Dictionary<ConversionItem, string> _formatsBeforeCompression = [];
    private readonly Dictionary<ConversionItem, int> _pendingQueueAnimations = [];
    private AppSettings _settings;
    private readonly DispatcherTimer _etaTimer;
    private IReadOnlyList<ConversionItem> _etaQueue = [];
    private ConversionItem? _etaCurrentItem;
    private DateTime _etaStartedAt;
    private TimeSpan _etaCurrentEstimate;

    public MainWindow()
    {
        _settings = SettingsService.Load();
        if (SettingsService.ConsumeInstallWelcomeMarker(out _isReinstallWelcome))
        {
            _settings.HasCompletedOnboarding = false;
            _settings.OnboardingVersion = 0;
            SettingsService.Save(_settings);
        }
        var materialBeforeNormalization = _settings.Material;
        ThemeService.NormalizeForPlatform(_settings);
        ThemeService.Apply(_settings);
        if (!string.Equals(materialBeforeNormalization, _settings.Material, StringComparison.Ordinal))
            SettingsService.Save(_settings);
        InitializeComponent();
        MotionService.PrepareWindow(this);
        DataContext = this;
        ApplyLanguage();
        ThemeService.ConfigureWindow(this, _settings);
        _etaTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _etaTimer.Tick += (_, _) => UpdateEta();
        SourceInitialized += MainWindow_SourceInitialized;
        ContentRendered += MainWindow_ContentRendered;
        var documentsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Converter+");
        var legacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Конвертер+");
        var legacyConversionDefault = Path.Combine(legacyRoot, "Конвертер");
        var legacyCompressionDefault = Path.Combine(legacyRoot, "Сжатие");
        if (string.IsNullOrWhiteSpace(_settings.LastConversionOutputFolder)
            && !string.IsNullOrWhiteSpace(_settings.LastOutputFolder)
            && !PathsEqual(_settings.LastOutputFolder, legacyRoot)
            && !PathsEqual(_settings.LastOutputFolder, legacyConversionDefault)
            && !PathsEqual(_settings.LastOutputFolder, legacyCompressionDefault))
            _settings.LastConversionOutputFolder = _settings.LastOutputFolder;
        if (PathsEqual(_settings.LastConversionOutputFolder, legacyConversionDefault))
            _settings.LastConversionOutputFolder = null;
        if (PathsEqual(_settings.LastCompressionOutputFolder, legacyCompressionDefault))
            _settings.LastCompressionOutputFolder = null;
        var conversionDefault = Path.Combine(documentsRoot, "Converter");
        var compressionDefault = Path.Combine(documentsRoot, "Compression");
        _conversionOutputFolder = _settings.RememberLastFolder && !string.IsNullOrWhiteSpace(_settings.LastConversionOutputFolder)
            ? _settings.LastConversionOutputFolder
            : conversionDefault;
        _compressionOutputFolder = _settings.RememberLastFolder && !string.IsNullOrWhiteSpace(_settings.LastCompressionOutputFolder)
            ? _settings.LastCompressionOutputFolder
            : compressionDefault;
        EnsureOutputFolder(_conversionOutputFolder);
        EnsureOutputFolder(_compressionOutputFolder);
        OutputFolderBox.Text = _conversionOutputFolder;
        ImageQualitySlider.Value = _settings.ImageQuality;
        _isInitializing = false;
        UpdateQualityLabel();
        UpdateTargetSizeUnitLabel();
        ConverterEngine.ImageQuality = _settings.ImageQuality;
        UpdateEngineSummary();
        RefreshQueue();
        SettingsService.Save(_settings);
    }

    private string T(string key) => LocalizationService.Get(key, _settings.Language);

    private void ApplyLanguage()
    {
        Title = "Converter+";
        AppTitle.Text = T(_compressionMode ? "compressTitle" : "appTitle");
        AppSubtitle.Text = T(_compressionMode ? "compressSubtitle" : "convertSubtitle");
        CompressionModeLabel.Text = T(_compressionMode ? "conversionMode" : "compressionMode");
        CompressionModeButton.ToolTip = T("switchMode");
        AutomationProperties.SetName(CompressionModeButton, T(_compressionMode ? "conversionMode" : "compressionMode"));
        SettingsButton.ToolTip = T("settingsTip");
        AutomationProperties.SetName(SettingsButton, T("settingsTip"));
        MinimizeButton.ToolTip = T("minimize");
        CloseButton.ToolTip = T("close");
        MaximizeButton.ToolTip = T(WindowState == WindowState.Maximized ? "restore" : "maximize");
        OutputFolderLabel.Text = T("outputFolder");
        OutputFolderBox.ToolTip = T("outputFolderTip");
        ChooseFolderLabel.Text = T("chooseFolder");
        FilesHeading.Text = T("files");
        AddFilesLabel.Text = T("addFiles");
        ClearLabel.Text = T("clear");
        DropTitle.Text = T(_compressionMode ? "dropCompress" : "dropConvert");
        DropHint.Text = T("dropHint");
        FormatsHint.Text = T("supportedFormats");
        FileColumnHeader.Text = T("headerFile");
        SizeColumnHeader.Text = T("headerSize");
        FormatColumnHeader.Text = T("headerFormat");
        StatusColumnHeader.Text = T("headerStatus");
        QualityLabel.Text = T("quality");
        QualityHint.Text = T("qualityHint");
        QualityHelpIcon.ToolTip = T("qualityTip");
        ImageQualitySlider.ToolTip = T("qualitySliderTip");
        QualityValue.ToolTip = T("qualityEntryTip");
        TargetSizeLabel.Text = T("targetSize");
        TargetSizeValue.ToolTip = T("targetSizeTip");
        TargetSizeUnitButton.ToolTip = T("unitTip");
        MegabytesMenuItem.Header = T("megabytes");
        GigabytesMenuItem.Header = T("gigabytes");
        CancelButton.Content = T("cancel");
        ConvertButtonLabel.Text = T(_compressionMode ? "compressButton" : "convertButton");
        AutomationProperties.SetName(MinimizeButton, T("minimize"));
        AutomationProperties.SetName(MaximizeButton, T(WindowState == WindowState.Maximized ? "restore" : "maximize"));
        AutomationProperties.SetName(CloseButton, T("close"));
        UpdateTargetSizeUnitLabel();
        if (!_isConverting)
            FooterStatus.Text = T("addFilesStart");
    }

    private void RelocalizeQueue()
    {
        foreach (var item in Files.Where(file => !file.IsBusy))
        {
            var status = item.Status;
            if (item.Kind == FileKind.Unsupported)
            {
                item.Status = T("unsupported");
            }
            else if (status is "Готов к конвертации" or "Ready to convert")
            {
                item.Status = T("readyForConversion");
            }
            else if (status is "Готово" or "Ready")
            {
                item.Status = T("ready");
            }
            else if (status.StartsWith("Готово, больше цели: ", StringComparison.Ordinal)
                || status.StartsWith("Done, over target: ", StringComparison.Ordinal))
            {
                var separator = status.IndexOf(':');
                item.Status = string.Format(T("readyOverTarget"), separator >= 0 ? status[(separator + 1)..].Trim() : status);
            }
            else if (status.StartsWith("Ошибка: ", StringComparison.Ordinal))
            {
                item.Status = string.Format(T("errorPrefix"), LocalizeError(status[8..]));
            }
            else if (status.StartsWith("Error: ", StringComparison.Ordinal))
            {
                item.Status = string.Format(T("errorPrefix"), LocalizeError(status[7..]));
            }
        }
    }

    private void UpdateTargetSizeUnitLabel() =>
        TargetSizeUnitLabel.Text = _sizeUnit == "GB"
            ? (LocalizationService.ResolveLanguage(_settings.Language) == "ru" ? "ГБ" : "GB")
            : (LocalizationService.ResolveLanguage(_settings.Language) == "ru" ? "МБ" : "MB");

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (_settings.HasCompletedOnboarding && _settings.OnboardingVersion >= 3)
        {
            MotionService.RevealWindow(this);
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!IsVisible || _settings.HasCompletedOnboarding && _settings.OnboardingVersion >= 3)
                return;

            var dialog = new OnboardingWindow(_isReinstallWelcome) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _settings.HasCompletedOnboarding = true;
                _settings.OnboardingVersion = 3;
                SettingsService.Save(_settings);
            }

            MotionService.RevealWindow(this);
        }));
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcHitTest || WindowState == WindowState.Maximized)
            return IntPtr.Zero;

        var screenX = unchecked((short)(long)lParam);
        var screenY = unchecked((short)((long)lParam >> 16));
        var point = PointFromScreen(new Point(screenX, screenY));
        var left = point.X <= ResizeGrip;
        var right = point.X >= ActualWidth - ResizeGrip;
        var top = point.Y <= ResizeGrip;
        var bottom = point.Y >= ActualHeight - ResizeGrip;
        var hit = (left, right, top, bottom) switch
        {
            (true, _, true, _) => HtTopLeft,
            (_, true, true, _) => HtTopRight,
            (true, _, _, true) => HtBottomLeft,
            (_, true, _, true) => HtBottomRight,
            (true, _, _, _) => HtLeft,
            (_, true, _, _) => HtRight,
            (_, _, true, _) => HtTop,
            (_, _, _, true) => HtBottom,
            _ => 0
        };
        if (hit == 0) return IntPtr.Zero;
        handled = true;
        return new IntPtr(hit);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            e.Handled = true;
            DragMove();
        }
    }

    private void WindowSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(WindowSurface);
        if (point.Y > 16 || e.OriginalSource is Button)
            return;

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            e.Handled = true;
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => PersistQualitySetting();

    private void CompressionMode_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting)
            return;

        RememberFolder();
        _compressionMode = !_compressionMode;
        ConverterEngine.CompressionMode = _compressionMode;
        QualityControl.Visibility = _compressionMode ? Visibility.Collapsed : Visibility.Visible;
        TargetSizeControl.Visibility = _compressionMode ? Visibility.Visible : Visibility.Collapsed;
        OutputFolderBox.Text = _compressionMode ? _compressionOutputFolder : _conversionOutputFolder;
        if (_compressionMode)
        {
            _formatsBeforeCompression.Clear();
            foreach (var item in Files)
            {
                item.IsCompressionMode = true;
                if (IsMediaOrImage(item))
                    SetCompressionTarget(item);
            }
        }
        else
        {
            foreach (var item in Files)
                item.IsCompressionMode = false;
            foreach (var (item, format) in _formatsBeforeCompression)
                item.TargetFormat = format;
            _formatsBeforeCompression.Clear();
        }

        ApplyLanguage();
        CompressionModeButton.SetResourceReference(Control.BackgroundProperty, _compressionMode ? "AccentLight" : "Surface");
        CompressionModeButton.SetResourceReference(Control.BorderBrushProperty, _compressionMode ? "Accent" : "Line");
        MotionService.Reveal(CompressionModeButton, replay: true, durationMilliseconds: 150, verticalOffset: 3);
        MotionService.Reveal(AppTitle, replay: true, durationMilliseconds: 150, verticalOffset: 0);
        MotionService.Reveal(_compressionMode ? TargetSizeControl : QualityControl,
            replay: true, durationMilliseconds: 170, verticalOffset: 3);
        RefreshQueue();
    }

    private void ImageQuality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing)
            return;

        if (!_updatingQualityText && QualityValue is not null && !QualityValue.IsKeyboardFocusWithin)
            UpdateQualityLabel();

        if (_settings is null || ImageQualitySlider is null)
            return;

        _settings.ImageQuality = (int)Math.Round(ImageQualitySlider.Value);
        ConverterEngine.ImageQuality = _settings.ImageQuality;
        _qualityDirty = true;
    }

    private void UpdateQualityLabel()
    {
        if (QualityValue is null || ImageQualitySlider is null || QualityValue.IsKeyboardFocusWithin)
            return;

        _updatingQualityText = true;
        QualityValue.Text = $"{ImageQualitySlider.Value:0}%";
        _updatingQualityText = false;
    }

    private void QualityValue_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!_updatingQualityText)
            QualityValue.SelectAll();
    }

    private void QualityValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        CommitQualityValue();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void QualityValue_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitQualityValue();
        PersistQualitySetting();
    }

    private void ImageQuality_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => PersistQualitySetting();

    private void QualitySlider_LostKeyboardFocus(object sender, RoutedEventArgs e) => PersistQualitySetting();

    private void PersistQualitySetting()
    {
        if (!_qualityDirty)
            return;
        SettingsService.Save(_settings);
        _qualityDirty = false;
    }

    private void CommitQualityValue()
    {
        var raw = QualityValue.Text.Trim().TrimEnd('%').Trim();
        var parsed = double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (parsed)
            ImageQualitySlider.Value = Math.Clamp(value, ImageQualitySlider.Minimum, ImageQualitySlider.Maximum);
        else
            UpdateQualityLabel();
    }

    private void TargetSize_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character) && character is not '.' and not ',');
    }

    private void TargetSize_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitializing && ConvertButton is not null)
        {
            TargetSizeValue.BorderBrush = (Brush)FindResource(TryGetTargetSizeBytes(out _) ? "Line" : "Danger");
            RefreshQueue();
        }
    }

    private void TargetSize_LostFocus(object sender, RoutedEventArgs e)
    {
        var maximum = _sizeUnit == "GB" ? 100 : 999;
        if (double.TryParse(TargetSizeValue.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            && value > maximum)
            TargetSizeValue.Text = maximum.ToString(CultureInfo.InvariantCulture);
        TargetSizeValue.BorderBrush = (Brush)FindResource(TryGetTargetSizeBytes(out _) ? "Line" : "Danger");
    }

    private void TargetSizeUnit_Click(object sender, RoutedEventArgs e)
    {
        var menu = TargetSizeUnitButton.ContextMenu;
        if (menu is null)
            return;
        menu.PlacementTarget = TargetSizeUnitButton;
        menu.HorizontalOffset = TargetSizeUnitButton.ActualWidth - 170;
        menu.IsOpen = true;
    }

    private void TargetSizeUnitMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string unit })
            return;
        _sizeUnit = unit;
        var limit = unit == "GB" ? 100 : 999;
        if (int.TryParse(TargetSizeValue.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > limit)
            TargetSizeValue.Text = limit.ToString(CultureInfo.InvariantCulture);
        UpdateTargetSizeUnitLabel();
        RefreshQueue();
    }

    private bool TryGetTargetSizeBytes(out long bytes)
    {
        bytes = 0;
        var valueText = TargetSizeValue?.Text.Trim();
        if (string.IsNullOrWhiteSpace(valueText))
            return false;
        var parsed = double.TryParse(valueText, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || double.TryParse(valueText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        var maximum = _sizeUnit == "GB" ? 100d : 999d;
        if (!parsed || value <= 0 || value > maximum || !double.IsFinite(value))
            return false;
        var unitMultiplier = _sizeUnit == "GB" ? 1024d * 1024 * 1024 : 1024d * 1024;
        if (value > long.MaxValue / unitMultiplier)
            return false;
        bytes = Math.Max(1, (long)Math.Round(value * unitMultiplier));
        return true;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized
        ? WindowState.Normal
        : WindowState.Maximized;

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton?.Content is TextBlock icon)
        {
            var maximized = WindowState == WindowState.Maximized;
            icon.Text = maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = T(maximized ? "restore" : "maximize");
            AutomationProperties.SetName(MaximizeButton, T(maximized ? "restore" : "maximize"));
        }
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("chooseFilesTitle"),
            Filter = $"{T("allFiles")}|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
            AddPaths(dialog.FileNames);
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = T("chooseOutputTitle"),
            InitialDirectory = Directory.Exists(OutputFolderBox.Text)
                ? OutputFolderBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputFolderBox.Text = dialog.FolderName;
            RememberFolder();
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            AddPaths(paths.Where(File.Exists));
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var knownPaths = Files.Select(file => file.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var animationIndex = 0;
        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (knownPaths.Add(path))
            {
                var item = new ConversionItem(path);
                item.Status = T(item.Kind == FileKind.Unsupported ? "unsupported" : "readyForConversion");
                item.IsCompressionMode = _compressionMode;
                Files.Add(item);
                _pendingQueueAnimations[item] = Math.Min(animationIndex++, 5) * 24;
                if (_compressionMode && IsMediaOrImage(item))
                    SetCompressionTarget(item);
            }
        }

        RefreshQueue();
    }

    private static bool IsMediaOrImage(ConversionItem item) =>
        item.Kind is FileKind.Image or FileKind.Audio or FileKind.Video;

    private void SetCompressionTarget(ConversionItem item)
    {
        _formatsBeforeCompression.TryAdd(item, item.TargetFormat);
        item.IsCompressionMode = true;
        item.TargetFormat = item.Kind switch
        {
            FileKind.Image when item.SourceExtension is "jpg" or "jpeg" or "webp" or "avif" or "gif" => item.SourceExtension,
            FileKind.Image => "webp",
            FileKind.Audio when item.SourceExtension is "mp3" or "aac" or "m4a" or "ogg" or "opus" => item.SourceExtension,
            FileKind.Audio => "mp3",
            FileKind.Video when item.SourceExtension is "mp4" or "mkv" or "mov" or "webm" or "avi" or "gif" => item.SourceExtension,
            FileKind.Video => "mp4",
            _ => item.SourceExtension
        };
    }

    private void QueueCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement card && card.DataContext is ConversionItem item)
        {
            if (_pendingQueueAnimations.Remove(item, out var delay))
                MotionService.Reveal(card, delay, durationMilliseconds: 170, verticalOffset: 4);
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ConversionItem item)
            Clipboard.SetText(item.SourcePath);
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            foreach (var item in menu.Items.OfType<MenuItem>().ToList())
            {
                var key = item.Tag?.ToString() switch
                {
                    "CopyPath" => "copyPath",
                    "Remove" => "removeQueue",
                    _ => null
                };
                if (key is not null)
                    item.Header = T(key);
            }
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConverting && (sender as FrameworkElement)?.DataContext is ConversionItem item)
        {
            Files.Remove(item);
            _pendingQueueAnimations.Remove(item);
            _formatsBeforeCompression.Remove(item);
            RefreshQueue();
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_isConverting) return;
        Files.Clear();
        _pendingQueueAnimations.Clear();
        _formatsBeforeCompression.Clear();
        RefreshQueue();
    }

    private async void Convert_Click(object sender, RoutedEventArgs e)
    {
        long? targetBytes = null;
        if (_compressionMode)
        {
            if (!TryGetTargetSizeBytes(out var parsedTargetBytes))
            {
                TargetSizeValue.BorderBrush = (Brush)FindResource("Danger");
            FooterStatus.Text = T("invalidTarget");
                return;
            }
            targetBytes = parsedTargetBytes;
        }

        var convertible = Files.Where(file => file.Kind != FileKind.Unsupported
            && (!_compressionMode || IsMediaOrImage(file))).ToList();
        if (convertible.Count == 0 || _isConverting) return;

        ConverterEngine.CompressionMode = _compressionMode;
        _isConverting = true;
        _conversionCts = new CancellationTokenSource();
        SetBusyState(true);
        var succeeded = 0;
        var failed = 0;
        var cancelled = false;

        try
        {
            Directory.CreateDirectory(OutputFolderBox.Text);
            RememberFolder();
            _etaQueue = convertible;
            _etaTimer.Start();
            for (var index = 0; index < convertible.Count; index++)
            {
                var item = convertible[index];
                if (_conversionCts.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                _etaCurrentItem = item;
                _etaQueue = convertible.Skip(index + 1).ToList();
                _etaStartedAt = DateTime.UtcNow;
                _etaCurrentEstimate = EstimateDuration(item);
                UpdateEta();
                item.IsBusy = true;
                item.Status = T(_compressionMode ? "compressing" : "converting");
                try
                {
                    var compressionFormat = item.TargetFormat;
                    if (targetBytes.HasValue && new FileInfo(item.SourcePath).Length <= targetBytes.Value)
                        item.TargetFormat = item.SourceExtension;
                    long outputBytes;
                    try
                    {
                        var outputPath = GetUniqueOutputPath(item);
                        outputBytes = await ConverterEngine.ConvertAsync(item, outputPath, _conversionCts.Token, targetBytes);
                    }
                    finally
                    {
                        if (targetBytes.HasValue)
                            item.TargetFormat = compressionFormat;
                    }
                    item.Status = targetBytes.HasValue && outputBytes > targetBytes.Value
                        ? string.Format(T("readyOverTarget"), FormatCatalog.FormatSize(outputBytes))
                        : T("ready");
                    succeeded++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = T("cancelled");
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    item.Status = string.Format(T("errorPrefix"), LocalizeError(ex.Message));
                    failed++;
                    FooterStatus.Text = string.Format(T("conversionError"), item.Name);
                }
                finally
                {
                    item.IsBusy = false;
                }
            }
        }
        catch (Exception ex)
        {
            FooterStatus.Text = string.Format(T("errorPrefix"), LocalizeError(ex.Message));
            failed++;
        }
        finally
        {
            _conversionCts?.Dispose();
            _conversionCts = null;
            _isConverting = false;
            _etaTimer.Stop();
            _etaQueue = [];
            _etaCurrentItem = null;
            SetBusyState(false);
            RefreshQueue();
        }

        if (cancelled)
            FooterStatus.Text = string.Format(T("stoppedCounts"), succeeded, failed);
        else
            FooterStatus.Text = failed == 0
                ? string.Format(T("convertedCount"), succeeded)
                : string.Format(T("finishedCounts"), succeeded, failed);

        if (succeeded > 0 && _settings.OpenOutputFolderAfterConversion)
        {
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(OutputFolderBox.Text);
            Process.Start(startInfo);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _conversionCts?.Cancel();

    private TimeSpan EstimateDuration(ConversionItem item)
    {
        double megabytes;
        try { megabytes = new FileInfo(item.SourcePath).Length / (1024d * 1024d); }
        catch { megabytes = 1; }

        var secondsPerMegabyte = item.Kind switch
        {
            FileKind.Video => 1.15,
            FileKind.Audio => 0.3,
            FileKind.Image => 0.12,
            _ => 0.16
        };
        var qualityFactor = 0.7 + _settings.ImageQuality / 100d * 0.6;
        var compressionFactor = _compressionMode ? 1.25 : 1;
        var seconds = (2 + megabytes * secondsPerMegabyte) * qualityFactor * compressionFactor;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 2, 12 * 60 * 60));
    }

    private void UpdateEta()
    {
        if (!_isConverting || _etaCurrentItem is null)
            return;

        var elapsed = DateTime.UtcNow - _etaStartedAt;
        var currentRemaining = _etaCurrentEstimate - elapsed;
        if (currentRemaining <= TimeSpan.Zero)
            currentRemaining = TimeSpan.FromSeconds(Math.Max(1, elapsed.TotalSeconds * 0.15));

        var secondsLeft = currentRemaining.TotalSeconds + _etaQueue.Sum(item => EstimateDuration(item).TotalSeconds);
        FooterStatus.Text = $"{T("processing")}: {_etaCurrentItem.Name}  ·  {T("eta")}: {FormatDuration(TimeSpan.FromSeconds(secondsLeft))}";
    }

    private string FormatDuration(TimeSpan duration)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(duration.TotalSeconds));
        if (seconds < 60)
            return string.Format(T("seconds"), seconds);
        if (seconds < 3600)
            return string.Format(T("minutesSeconds"), seconds / 60, seconds % 60);
        return string.Format(T("hoursMinutes"), seconds / 3600, (seconds % 3600) / 60);
    }

    private string LocalizeError(string message)
    {
        if (message.Contains("Этот тип файла пока не поддерживается", StringComparison.Ordinal))
            return T("unsupported");
        if (message.Contains("Для аудио и видео нужен FFmpeg", StringComparison.Ordinal))
            return T("ffmpegError");
        if (message.Contains("Не удалось определить длительность видео", StringComparison.Ordinal))
            return T("videoDurationError");
        if (message.Contains("Для документов установите бесплатный LibreOffice", StringComparison.Ordinal))
            return T("officeError");
        if (message.Contains("LibreOffice не создал файл", StringComparison.Ordinal))
            return T("officeOutputError");
        const string unsupportedSuffix = " не поддерживается.";
        if (message.StartsWith("Формат аудио ", StringComparison.Ordinal) && message.EndsWith(unsupportedSuffix, StringComparison.Ordinal))
            return string.Format(T("audioFormatError"), message[13..^unsupportedSuffix.Length]);
        if (message.StartsWith("Формат видео ", StringComparison.Ordinal) && message.EndsWith(unsupportedSuffix, StringComparison.Ordinal))
            return string.Format(T("videoFormatError"), message[13..^unsupportedSuffix.Length]);
        return message;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        PersistQualitySetting();
        var dialog = new SettingsWindow(_settings.Clone()) { Owner = this };
        dialog.SettingsApplied += (_, _) =>
        {
            var previousLanguage = _settings.Language;
            ThemeService.NormalizeForPlatform(dialog.Settings);
            ApplySettings(dialog.Settings);
            if (!string.Equals(LocalizationService.ResolveLanguage(previousLanguage), LocalizationService.ResolveLanguage(_settings.Language), StringComparison.Ordinal))
                RelocalizeQueue();
            ThemeService.ConfigureWindow(dialog, _settings);
        };
        dialog.ShowDialog();
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        try
        {
            ThemeService.NormalizeForPlatform(_settings);
            SettingsService.Save(_settings);
            ThemeService.Apply(_settings);
            ThemeService.ConfigureWindow(this, _settings);
            ApplyLanguage();
            ImageQualitySlider.Value = _settings.ImageQuality;
            UpdateQualityLabel();
            ConverterEngine.ImageQuality = _settings.ImageQuality;
            RememberFolder();
            UpdateEngineSummary();
            RefreshQueue();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, string.Format(T("saveSettingsError"), ex.Message), T("appTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RememberFolder()
    {
        if (_compressionMode)
            _compressionOutputFolder = OutputFolderBox.Text;
        else
            _conversionOutputFolder = OutputFolderBox.Text;
        _settings.LastCompressionOutputFolder = _compressionOutputFolder;
        _settings.LastConversionOutputFolder = _conversionOutputFolder;
        _settings.LastOutputFolder = OutputFolderBox.Text;
        if (_settings.RememberLastFolder)
            SettingsService.Save(_settings);
    }

    private static void EnsureOutputFolder(string path)
    {
        try { Directory.CreateDirectory(path); } catch { }
    }

    private void InstallDocs_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://www.libreoffice.org/download/download/") { UseShellExecute = true });
    }

    private string GetUniqueOutputPath(ConversionItem item)
    {
        var stem = Path.GetFileNameWithoutExtension(item.Name);
        var extension = item.TargetFormat.TrimStart('.').ToLowerInvariant();
        var candidate = Path.Combine(OutputFolderBox.Text, $"{stem}.{extension}");
        var suffix = 2;
        while (File.Exists(candidate))
            candidate = Path.Combine(OutputFolderBox.Text, $"{stem} ({suffix++}).{extension}");
        return candidate;
    }

    private void RefreshQueue()
    {
        var count = Files.Count;
        var isEmpty = count == 0;
        DropPanel.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (isEmpty != _queueWasEmpty)
        {
            if (isEmpty)
                MotionService.Reveal(DropPanel, replay: true, durationMilliseconds: 170, verticalOffset: 4);
            _queueWasEmpty = isEmpty;
        }
        QueueCount.Text = count == 0 ? T("queueEmpty") : string.Format(T("filesInQueue"), count);
        ClearButton.IsEnabled = count > 0 && !_isConverting;
        var eligibleCount = Files.Count(file => file.Kind != FileKind.Unsupported && (!_compressionMode || IsMediaOrImage(file)));
        var targetSizeIsValid = !_compressionMode || TryGetTargetSizeBytes(out _);
        ConvertButton.IsEnabled = eligibleCount > 0 && targetSizeIsValid && !_isConverting;
        var showConvert = count > 0;
        var convertWasVisible = ConvertButton.Visibility == Visibility.Visible;
        ConvertButton.Visibility = showConvert ? Visibility.Visible : Visibility.Collapsed;
        if (showConvert && !convertWasVisible)
            MotionService.Reveal(ConvertButton, replay: true);

        var hasOffice = Files.Any(file => file.Kind is FileKind.TextDocument or FileKind.Spreadsheet or FileKind.Presentation or FileKind.Pdf);
        InstallDocsButton.Visibility = hasOffice && !ConverterEngine.HasLibreOffice ? Visibility.Visible : Visibility.Collapsed;
        if (!_isConverting)
        {
            var unsupported = Files.Count(file => file.Kind == FileKind.Unsupported);
            FooterStatus.Text = unsupported > 0
                ? string.Format(T("unsupportedCount"), count, unsupported)
                : count == 0
                    ? T("addFilesStart")
                    : _compressionMode
                        ? Files.Any(file => !IsMediaOrImage(file))
                            ? T("compressionSkipped")
                            : T("compressionStatus")
                        : T("conversionStatus");
            StatusDot.Fill = unsupported > 0 ? (Brush)FindResource("Warn") : (Brush)FindResource("Accent");
        }
    }

    private void SetBusyState(bool isBusy)
    {
        AddFilesButton.IsEnabled = !isBusy;
        AddFilesButton.Visibility = Visibility.Visible;
        SettingsButton.IsEnabled = !isBusy;
        CompressionModeButton.IsEnabled = !isBusy;
        QualityControl.IsEnabled = !isBusy;
        TargetSizeControl.IsEnabled = !isBusy;
        ClearButton.IsEnabled = !isBusy && Files.Count > 0;
        ConvertButton.IsEnabled = !isBusy && Files.Any(file => file.Kind != FileKind.Unsupported
            && (!_compressionMode || IsMediaOrImage(file)))
            && (!_compressionMode || TryGetTargetSizeBytes(out _));
        CancelButton.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        FilesList.IsEnabled = !isBusy;
        StatusDot.Fill = isBusy ? (Brush)FindResource("Warn") : (Brush)FindResource("Accent");
    }

    private void UpdateEngineSummary()
    {
        var mediaReady = ConverterEngine.HasFfmpeg;
        var documentsReady = ConverterEngine.HasLibreOffice;
        MediaEngineSummary.Text = T(mediaReady ? "mediaReady" : "ffmpegMissing");
        DocumentEngineSummary.Text = T(documentsReady ? "officeReady" : "libreOfficeNeeded");
        MediaEngineDot.Fill = mediaReady ? (Brush)FindResource("Accent") : (Brush)FindResource("Warn");
        DocumentEngineDot.Fill = documentsReady ? (Brush)FindResource("Accent") : (Brush)FindResource("Warn");
    }
}
