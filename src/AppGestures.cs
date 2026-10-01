using Avalonia.Input;

namespace Sextant;

static class AppGestures
{
    public static KeyModifiers Command { get; } =
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public static KeyGesture CommandKey(Key key, KeyModifiers extra = KeyModifiers.None) =>
        new(key, Command | extra);

    public static bool Matches(KeyEventArgs e, Key key, KeyModifiers extra = KeyModifiers.None) =>
        e.Key == key && e.KeyModifiers == (Command | extra);
}
