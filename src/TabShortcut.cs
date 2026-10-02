using System.Globalization;

namespace Sextant;

/// <summary>Tabs 1–9 use the digits 1–9. Digit 0 is tab 10.</summary>
public static class TabShortcut
{
    public static int? IndexFromDigit(int digit) => digit switch
    {
        >= 1 and <= 9 => digit - 1,
        0 => 9,
        _ => null,
    };

    public static string? Hint(int index)
    {
        if (index is < 0 or > 9)
            return null;
        var key = index == 9 ? "0" : (index + 1).ToString(CultureInfo.InvariantCulture);
        return OperatingSystem.IsMacOS() ? "⌘" + key : "Ctrl+" + key;
    }
}
