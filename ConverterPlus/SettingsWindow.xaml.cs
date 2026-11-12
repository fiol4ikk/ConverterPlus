using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Navigation;
using ConverterPlus.Models;
using ConverterPlus.Services;

namespace ConverterPlus;

public partial class SettingsWindow : Window
{
    private const string LatestReleaseApi = "https://api.github.com/repos/fiol4ikk/ConverterPlus/releases/latest";
    private const string ReleasesPage = "https://github.com/fiol4ikk/ConverterPlus/releases";
    private static readonly HttpClient UpdateClient = CreateUpdateClient();
    private readonly Version _currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    private string _releaseUrl = ReleasesPage;
    private const int WmNcHitTest = 0x0084;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const double ResizeGrip = 4;
    public AppSettings Settings { get; private set; }
    public event EventHandler? SettingsApplied;

    public SettingsWindow(AppSettings settings)
    {
        Settings = settings;
        InitializeComponent();
        DataContext = Settings;
        SetAccentSwatch(Settings.AccentColor);
        ApplyLanguage();
        MotionService.PrepareWindow(this);
        ThemeService.ConfigureWindow(this, Settings);
        SourceInitialized += SettingsWindow_SourceInitialized;
        ContentRendered += (_, _) => MotionService.RevealWindow(this);
    }

    private void SettingsWindow_SourceInitialized(object? sender, EventArgs e)
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
            (true, _, _, _) => 10,
            (_, true, _, _) => 11,
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Accent_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is AppSettings settings && sender is RadioButton { Tag: string accent })
            settings.AccentColor = accent;
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized)
            ApplyLanguage();
    }

    private string T(string key) => LocalizationService.Get(key, Settings.Language);

    private void ApplyLanguage()
    {
        Title = T("settings");
        SettingsTitle.Text = T("settings");
        SettingsSubtitle.Text = T("settingsSubtitle");
        MinimizeSettingsButton.ToolTip = T("minimize");
        CloseSettingsCaptionButton.ToolTip = T("close");
        LanguageLabel.Text = T("language");
        LanguageSelector.ToolTip = T("languageTip");
        if (LanguageSelector.Items.Count >= 3)
        {
            ((ComboBoxItem)LanguageSelector.Items[0]).Content = T("autoLanguage");
            ((ComboBoxItem)LanguageSelector.Items[1]).Content = T("english");
            ((ComboBoxItem)LanguageSelector.Items[2]).Content = T("russian");
        }
        PersonalizationLabel.Text = T("personalization");
        ThemeLabel.Text = T("theme");
        ThemeSystemItem.Content = T("themeSystem");
        ThemeLightItem.Content = T("themeLight");
        ThemeDarkItem.Content = T("themeDark");
        MaterialLabel.Text = T("material");
        MaterialSolidItem.Content = T("solid");
        AccentLabel.Text = T("accent");
        BehaviorLabel.Text = T("behavior");
        RememberFolderCheck.Content = T("rememberFolder");
        OpenFolderCheck.Content = T("openFolderAfter");
        DeveloperAboutText.Text = T("developerAbout");
        DonationAboutText.Text = T("donationLinkAbout");
        UpdatesLabel.Text = T("updates");
        CurrentVersionLabel.Text = string.Format(T("currentVersion"), _currentVersion.ToString(3));
        CheckUpdatesButton.Content = T("checkUpdates");
        CheckUpdatesButton.ToolTip = T("updateCheckTip");
        OpenReleaseButton.Content = T("openRelease");
        DefaultsButton.Content = T("defaults");
        CloseSettingsButton.Content = T("closeSettings");
        ApplySettingsButton.Content = T("apply");

        SetAccentName(TealAccent, "green");
        SetAccentName(BlueAccent, "blue");
        SetAccentName(RoseAccent, "rose");
        SetAccentName(OrangeAccent, "orange");
        SetAccentName(VioletAccent, "violet");
    }

    private void SetAccentName(RadioButton button, string key)
    {
        var label = T(key);
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var previous = Settings;
        Settings = new AppSettings
        {
            Language = "Auto",
            LastOutputFolder = previous.LastOutputFolder,
            LastConversionOutputFolder = previous.LastConversionOutputFolder,
            LastCompressionOutputFolder = previous.LastCompressionOutputFolder,
            HasCompletedOnboarding = previous.HasCompletedOnboarding,
            OnboardingVersion = previous.OnboardingVersion
        };
        DataContext = Settings;
        SetAccentSwatch(Settings.AccentColor);
        ApplyLanguage();
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => SettingsApplied?.Invoke(this, EventArgs.Empty);

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        OpenReleaseButton.Visibility = Visibility.Collapsed;
        UpdateStatusText.Text = T("checkingUpdates");
        try
        {
            using var response = await UpdateClient.GetAsync(LatestReleaseApi);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Missing release tag.");
            _releaseUrl = root.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString() ?? ReleasesPage
                : ReleasesPage;
            var releaseVersionText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(releaseVersionText, out var releaseVersion))
                throw new InvalidDataException($"Unrecognized release version: {tag}");

            if (releaseVersion > _currentVersion)
            {
                UpdateStatusText.Text = string.Format(T("updateAvailable"), releaseVersion);
                OpenReleaseButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatusText.Text = T("noUpdates");
                _releaseUrl = ReleasesPage;
                OpenReleaseButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or KeyNotFoundException)
        {
            UpdateStatusText.Text = string.Format(T("updateCheckFailed"), ex.Message);
            _releaseUrl = ReleasesPage;
            OpenReleaseButton.Visibility = Visibility.Visible;
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });

    private static HttpClient CreateUpdateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ConverterPlus-UpdateChecker/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private void DeveloperLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void SetAccentSwatch(string accent) => (accent switch
    {
        "Blue" => BlueAccent,
        "Rose" => RoseAccent,
        "Orange" => OrangeAccent,
        "Violet" => VioletAccent,
        _ => TealAccent
    }).IsChecked = true;
}
