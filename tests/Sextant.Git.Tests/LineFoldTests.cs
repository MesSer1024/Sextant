using Sextant;

namespace Sextant.Git.Tests;

public class LineFoldTests
{
    [Fact]
    public void Short_line_stays_one_row()
    {
        Assert.Equal(1, LineFold.Count(""));
        Assert.Equal("", LineFold.Piece("", 0));
        Assert.Null(LineFold.Piece("", 1));
        Assert.Equal(1, LineFold.Count("abc"));
        Assert.Equal("abc", LineFold.Piece("abc", 0));
        Assert.Null(LineFold.Piece("abc", 1));
    }

    [Fact]
    public void Long_line_splits_without_dropping_characters()
    {
        var text = new string('a', LineFold.Columns) + "bc";
        Assert.Equal(2, LineFold.Count(text));
        Assert.Equal(new string('a', LineFold.Columns), LineFold.Piece(text, 0));
        Assert.Equal("bc", LineFold.Piece(text, 1));
        Assert.Null(LineFold.Piece(text, 2));
        Assert.Equal(text, LineFold.Piece(text, 0) + LineFold.Piece(text, 1));
    }
}
