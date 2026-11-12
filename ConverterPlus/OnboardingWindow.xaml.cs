using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ConverterPlus.Services;

namespace ConverterPlus;

public partial class OnboardingWindow : Window
{
    private int _page;
    private readonly string _language;
    private readonly bool _isReinstall;

    public OnboardingWindow(bool isReinstall = false)
    {
        var settings = SettingsService.Load();
        _language = settings.Language;
        _isReinstall = isReinstall;
        InitializeComponent();
        MotionService.PrepareWindow(this);
        UpdatePage(animate: false);
        ThemeService.ConfigureWindow(this, settings);
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(WindowProc);
        };
        ContentRendered += (_, _) =>
        {
            MotionService.RevealWindow(this);
            PageTitle.Focus();
        };
    }

    private void UpdatePage(bool animate = true)
    {
        OnboardingAppTitle.Text = T("appTitle");
        OnboardingSubtitle.Text = T("quickStart");
        Title = "Converter+";
        var keys = _page switch
        {
            0 => ("welcome", "welcomeBody", "weakPcWarning"),
            1 => ("appearancePage", "appearanceBody", "appearanceDetail"),
            2 => ("converterPage", "converterBody", "converterDetail"),
            3 => ("compressionPage", "compressionBody", "compressionDetail"),
            _ => ("finalPage", _isReinstall ? "finalReinstallBody" : "finalBody", "finalDetail")
        };
        PageTitle.Text = T(keys.Item1);
        PageBody.Text = T(keys.Item2);
        PageDetail.Text = T(keys.Item3);
        BackButton.Visibility = _page == 0 ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Content = T("back");
        NextButton.Content = _page == 4 ? T("start") : T("next");
        PageDot1.Fill = DotBrush(0);
        PageDot2.Fill = DotBrush(1);
        PageDot3.Fill = DotBrush(2);
        PageDot4.Fill = DotBrush(3);
        PageDot5.Fill = DotBrush(4);
        LinksPanel.Visibility = _page == 4 ? Visibility.Visible : Visibility.Collapsed;
        DeveloperLinkLabel.Text = T("developerLinkAbout");
        DonationLinkLabel.Text = T("donationLinkAbout");
        if (animate)
        {
            MotionService.Reveal(PageTitle, replay: true, durationMilliseconds: 175, verticalOffset: 4);
            MotionService.Reveal(PageBody, 25, replay: true, durationMilliseconds: 185, verticalOffset: 4);
            MotionService.Reveal(PageDetail, 50, replay: true, durationMilliseconds: 195, verticalOffset: 4);
        }
    }

    private string T(string key) => LocalizationService.Get(key, _language);

    private Brush DotBrush(int index) => (Brush)FindResource(index == _page ? "Accent" : "Line");

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page < 4)
        {
            _page++;
            UpdatePage();
            return;
        }

        DialogResult = true;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_page == 0)
            return;
        _page--;
        UpdatePage();
    }

    private void WindowSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button)
            return;
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            e.Handled = true;
            DragMove();
        }
    }

    private void Link_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmNcHitTest = 0x0084;
        const int resizeGrip = 4;
        if (message != WmNcHitTest)
            return IntPtr.Zero;

        var x = unchecked((short)(long)lParam);
        var y = unchecked((short)((long)lParam >> 16));
        var point = PointFromScreen(new Point(x, y));
        var left = point.X <= resizeGrip;
        var right = point.X >= ActualWidth - resizeGrip;
        var top = point.Y <= resizeGrip;
        var bottom = point.Y >= ActualHeight - resizeGrip;
        var hit = (left, right, top, bottom) switch
        {
            (true, _, true, _) => 13,
            (_, true, true, _) => 14,
            (true, _, _, true) => 16,
            (_, true, _, true) => 17,
            (true, _, _, _) => 10,
            (_, true, _, _) => 11,
            (_, _, true, _) => 12,
            (_, _, _, true) => 15,
            _ => 0
        };
        if (hit == 0)
            return IntPtr.Zero;
        handled = true;
        return new IntPtr(hit);
    }
}
