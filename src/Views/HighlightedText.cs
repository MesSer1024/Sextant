using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Sextant;

namespace Sextant.Views;

public class HighlightedText : CopyableText
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<HighlightedText, string?>(nameof(Source));

    public static readonly StyledProperty<string?> LanguageProperty =
        AvaloniaProperty.Register<HighlightedText, string?>(nameof(Language));

    private static readonly IBrush DarkKeyword = Freeze(Color.Parse("#79B8FF"));
    private static readonly IBrush LightKeyword = Freeze(Color.Parse("#0000FF"));
    private static readonly IBrush DarkString = Freeze(Color.Parse("#CE9178"));
    private static readonly IBrush LightString = Freeze(Color.Parse("#A31515"));
    private static readonly IBrush DarkComment = Freeze(Color.Parse("#6A9955"));
    private static readonly IBrush LightComment = Freeze(Color.Parse("#008000"));
    private static readonly IBrush DarkNumber = Freeze(Color.Parse("#B5CEA8"));
    private static readonly IBrush LightNumber = Freeze(Color.Parse("#098658"));
    private int _paint;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == LanguageProperty || change.Property.Name == nameof(ActualThemeVariant))
            Paint();
    }

    public HighlightedText()
    {
        // A row tokenizes when the virtualizing panel realizes it. Off-screen lines stay plain data.
    }

    protected override Type StyleKeyOverride => typeof(CopyableText);

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string? Language
    {
        get => GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    private void Paint()
    {
        if (_paint > 0)
            return;
        var text = Source ?? "";
        // A row is folded before it is shown. A longer value still must not build a run per character.
        if (text.Length > LineFold.Columns)
            text = text[..LineFold.Columns];
        var spans = DiffSyntax.Tokenize(text, Language);
        var colored = false;
        foreach (var span in spans)
        {
            if (span.Kind != SyntaxKind.Plain)
            {
                colored = true;
                break;
            }
        }

        _paint++;
        try
        {
            if (!colored)
            {
                PlainLength = -1;
                if (Inlines is not null)
                    Inlines = null;
                Text = text;
                return;
            }

            PlainLength = text.Length;
            // InlineCollection.Add copies TextBlock.Text into a leading plain Run when the
            // collection is already hosted. A plain pass, or a recycled row, leaves that text
            // set, so the same line is drawn twice: once with no color and once highlighted.
            if (Inlines is { Count: > 0 })
                Inlines.Clear();
            if (Text is not null)
                Text = null;

            var inlines = new InlineCollection();
            var dark = ActualThemeVariant == ThemeVariant.Dark;
            foreach (var span in spans)
            {
                if (span.Length <= 0 || span.Start < 0 || span.Start + span.Length > text.Length)
                    continue;
                var run = new Run(text.Substring(span.Start, span.Length));
                if (Brush(span.Kind, dark) is { } brush)
                    run.Foreground = brush;
                inlines.Add(run);
            }

            Inlines = inlines;
        }
        finally
        {
            _paint--;
        }
    }

    private static IBrush? Brush(SyntaxKind kind, bool dark) => kind switch
    {
        SyntaxKind.Keyword => dark ? DarkKeyword : LightKeyword,
        SyntaxKind.String => dark ? DarkString : LightString,
        SyntaxKind.Comment => dark ? DarkComment : LightComment,
        SyntaxKind.Number => dark ? DarkNumber : LightNumber,
        _ => null,
    };

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        return brush;
    }
}
