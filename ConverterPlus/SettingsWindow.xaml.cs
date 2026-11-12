using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Diagnostics;
using System.Windows.Navigation;
using ConverterPlus.Models;
using ConverterPlus.Services;

namespace ConverterPlus;

public partial class SettingsWindow : Window
{
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
        DefaultsButton.Content = T("defaults");
        CloseSettingsButton.Content = T("closeSettings");
        ApplySettingsButton.Content = T("apply");

        SetAccentName(TealAccent, "teal");
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
