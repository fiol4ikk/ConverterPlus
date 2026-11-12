using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ConverterPlus.Models;

namespace ConverterPlus.Services;

internal static class ThemeService
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableBlurBehind = 3;
    private const int AccentEnableAcrylicBlurBehind = 4;

    public static void Apply(AppSettings settings)
    {
        var dark = settings.ThemeMode switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsSystemDark()
        };
        var windows11 = IsWindows11();
        var win10Mica = !windows11 && settings.Material == "Mica";
        var acrylic = windows11 && settings.Material == "Acrylic";
        var mica = windows11 && settings.Material == "Mica";
        var blurMaterial = acrylic || win10Mica;
        var transparent = blurMaterial || mica;
        var accent = GetAccent(settings.AccentColor);
        var accentLight = Color.FromArgb(dark ? (byte)48 : (byte)38, accent.R, accent.G, accent.B);
        var baseSurface = ColorFromHex(dark ? "FF171B1F" : "FFF2F5F6");

        SetBrush("WindowSurface", blurMaterial
            ? ColorFromHex(dark ? "01FFFFFF" : "01000000")
            : baseSurface);
        SetBrush("PopupSurface", ColorFromHex(dark ? "FF282F35" : "FFF8FAFB"));
        SetBrush("SliderTrackActive", accent);
        SetBrush("SliderTrackInactive", ColorFromHex(dark ? "66576268" : transparent ? "887E8C91" : "FFE1E7E9"));
        SetBrush("SliderThumbFill", ColorFromHex(dark ? "FF30373D" : "FFFFFFFF"));
        SetBrush("SliderThumbBorder", accent);

        SetBrush("Canvas", blurMaterial
            ? ColorFromHex(dark ? "00171B1F" : "001C2226")
            : baseSurface);
        SetBrush("Surface", ColorFromHex(dark
            ? transparent ? blurMaterial ? "7821272D" : "DD20262B" : "FF20262B"
            : transparent ? blurMaterial ? "78FFFFFF" : "E0FFFFFF" : "FFFFFFFF"));
        SetBrush("SurfaceRaised", ColorFromHex(dark
            ? transparent ? blurMaterial ? "8C2A3036" : "EB2A3036" : "FF30373D"
            : transparent ? blurMaterial ? "8CFFFFFF" : "F0FFFFFF" : "FFFFFFFF"));
        SetBrush("SurfaceSubtle", ColorFromHex(dark
            ? transparent ? blurMaterial ? "681D2328" : "D51D2328" : "FF282F35"
            : transparent ? blurMaterial ? "68EEF3F4" : "D7F2F6F7" : "FFF6F8F9"));
        SetBrush("FileBadge", ColorFromHex(dark
            ? transparent ? blurMaterial ? "88303A40" : "DB303A40" : "FF303A40"
            : transparent ? blurMaterial ? "88E2EBEC" : "DDE8F0F0" : "FFEEF3F3"));
        SetBrush("Line", ColorFromHex(dark ? "66576268" : transparent ? "887E8C91" : "FFE1E7E9"));
        SetBrush("Ink", ColorFromHex(dark ? "FFF2F4F5" : "FF1D252B"));
        SetBrush("Muted", ColorFromHex(dark ? "FFB5C0C5" : "FF66757E"));
        SetBrush("Accent", accent);
        SetBrush("AccentLight", accentLight);
        SetBrush("AccentHover", ColorFromHex(settings.AccentColor switch
        {
            "Blue" => dark ? "FF3A91D8" : "FF005A9E",
            "Rose" => dark ? "FFE56D93" : "FFA82F5A",
            "Orange" => dark ? "FFE58A35" : "FF914500",
            "Violet" => dark ? "FFB69CFF" : "FF7040D8",
            _ => dark ? "FF35D39A" : "FF008A5B"
        }));
        SetBrush("HoverSurface", ColorFromHex(dark ? "E8343C42" : transparent ? blurMaterial ? "78E7F0ED" : "DDF0F5F4" : "FFF0F5F4"));
        SetBrush("SelectedSurface", ColorFromHex(dark
            ? transparent ? "6651B9A5" : "FF24483F"
            : transparent ? "6251B9A5" : "FFD7ECE8"));
        SetBrush("Warn", ColorFromHex(dark ? "FFFFC46B" : "FF9B5A0A"));
        SetBrush("Danger", ColorFromHex(dark ? "FFFF8585" : "FFB84242"));
        SetBrush("TextOnAccent", Colors.White);
        SetBrush("HeaderSurface", ColorFromHex(dark
            ? transparent ? blurMaterial ? "7820252A" : "DD20252A" : "FF20252A"
            : transparent ? blurMaterial ? "78FFFFFF" : "DCFFFFFF" : "FFFFFFFF"));
        SetBrush("DropSurface", ColorFromHex(dark
            ? transparent ? blurMaterial ? "581E2429" : "CC1E2429" : "FF1E2429"
            : transparent ? blurMaterial ? "58F7FAFB" : "D6FBFCFD" : "FFFBFCFD"));

    }

    public static void ConfigureWindow(Window window, AppSettings settings)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            window.SourceInitialized += (_, _) => ConfigureWindow(window, settings);
            return;
        }

        try
        {
            var windows11 = IsWindows11();
            var useMica = settings.Material == "Mica" && windows11;
            var useWin10Mica = settings.Material == "Mica" && !windows11;
            var useAcrylic = settings.Material == "Acrylic" && windows11;
            var backdrop = useMica ? 2 : 1;
            var dark = settings.ThemeMode == "Dark" || settings.ThemeMode == "System" && IsSystemDark() ? 1 : 0;
            var rounded = 2;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
                DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref rounded, sizeof(int));
                if (useMica)
                {
                    var glass = new Margins(-1, -1, -1, -1);
                    DwmExtendFrameIntoClientArea(handle, ref glass);
                }
                else
                {
                    var glass = new Margins();
                    DwmExtendFrameIntoClientArea(handle, ref glass);
                }
            }

            ApplyAccentBackdrop(handle, useAcrylic || useWin10Mica,
                useWin10Mica ? AccentEnableBlurBehind : AccentEnableAcrylicBlurBehind,
                dark == 1 ? unchecked((int)0xB820252A) : unchecked((int)0xB8F5F7F8));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWindows11() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public static void NormalizeForPlatform(AppSettings settings)
    {
        if (settings.Material == "LiquidGlass")
            settings.Material = IsWindows11() ? "Acrylic" : "Mica";
        if (!IsWindows11() && settings.Material == "Acrylic")
            settings.Material = "Mica";
    }

    private static Color GetAccent(string name) => ColorFromHex(name switch
    {
        "Blue" => "FF0078D4",
        "Rose" => "FFC43B6B",
        "Orange" => "FFB45A00",
        "Violet" => "FF8B5CF6",
        "Teal" => "FF10B981",
        _ => "FF8B5CF6"
    });

    private static Color ColorFromHex(string value) => (Color)ColorConverter.ConvertFromString($"#{value}")!;

    private static void SetBrush(string key, Color color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);

    private static void ApplyAccentBackdrop(IntPtr handle, bool enabled, int enabledState, int gradientColor)
    {
        var policy = new AccentPolicy
        {
            State = enabled ? enabledState : AccentDisabled,
            Flags = 2,
            GradientColor = gradientColor
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var dataPointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, dataPointer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = dataPointer,
                SizeOfData = size
            };
            SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(dataPointer);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll")]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int State;
        public int Flags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins(int left, int right, int top, int bottom)
    {
        public int Left = left;
        public int Right = right;
        public int Top = top;
        public int Bottom = bottom;
    }
}
