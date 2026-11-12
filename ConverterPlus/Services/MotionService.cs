using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConverterPlus.Services;

internal static class MotionService
{
    private static readonly ConditionalWeakTable<FrameworkElement, object> RevealedElements = new();
    private static readonly ConditionalWeakTable<Window, object> RevealedWindows = new();

    public static void PrepareWindow(Window window)
    {
        window.Opacity = SystemParameters.ClientAreaAnimation ? 0 : 1;
    }

    public static void RevealWindow(Window window)
    {
        if (RevealedWindows.TryGetValue(window, out _))
            return;

        RevealedWindows.Add(window, new object());
        if (!SystemParameters.ClientAreaAnimation)
        {
            window.Opacity = 1;
            return;
        }

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) =>
        {
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = 1;
        };
        window.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    public static void Reveal(
        FrameworkElement element,
        int delayMilliseconds = 0,
        bool replay = false,
        int durationMilliseconds = 190,
        double verticalOffset = 6)
    {
        if (replay)
            RevealedElements.Remove(element);

        if (RevealedElements.TryGetValue(element, out _))
            return;

        RevealedElements.Add(element, new object());
        if (!SystemParameters.ClientAreaAnimation)
        {
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 0;
        var transform = new TranslateTransform(0, verticalOffset);
        element.RenderTransform = transform;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var opacity = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
            EasingFunction = easing
        };
        var slide = new DoubleAnimation(verticalOffset, 0, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
            EasingFunction = easing
        };

        opacity.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            element.Opacity = 1;
            transform.Y = 0;
            element.RenderTransform = Transform.Identity;
        };
        element.BeginAnimation(UIElement.OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
    }
}
