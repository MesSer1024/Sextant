namespace Sextant.Git.Tests;

public class LineCopyTests
{
    [Fact]
    public void Selection_copies_part_of_one_line()
    {
        string?[] lines = ["alpha beta"];
        Assert.Equal("pha be", LineCopy.Join(lines, 0, 2, 0, 8));
        Assert.Equal("pha be", LineCopy.Join(lines, 0, 8, 0, 2));
        Assert.Equal("", LineCopy.Join(lines, 0, 3, 0, 3));
        Assert.Equal((2, 8), LineCopy.Slice(lines, 0, 2, 0, 8, 0));
        Assert.Null(LineCopy.Slice(lines, 0, 3, 0, 3, 0));
    }

    [Fact]
    public void Selection_crosses_lines_and_skips_rows_from_another_column()
    {
        string?[] lines = ["one", null, "two two", ""];
        Assert.Equal("ne\ntwo t", LineCopy.Join(lines, 0, 1, 2, 5));
        Assert.Equal("ne\ntwo two\n", LineCopy.Join(lines, 0, 1, 3, 0));
        Assert.Equal((1, 3), LineCopy.Slice(lines, 0, 1, 2, 5, 0));
        Assert.Null(LineCopy.Slice(lines, 0, 1, 2, 5, 1));
        Assert.Equal((0, 5), LineCopy.Slice(lines, 0, 1, 2, 5, 2));
        Assert.Null(LineCopy.Slice(lines, 0, 1, 2, 5, 3));
    }

    [Fact]
    public void Select_all_keeps_blank_lines()
    {
        string?[] lines = ["a", "", "c"];
        Assert.Equal("a\n\nc", LineCopy.Join(lines, 0, 0, 2, 1));
        Assert.Null(LineCopy.Slice(lines, 0, 0, 2, 1, 1));
        Assert.Equal((0, 1), LineCopy.Slice(lines, 0, 0, 2, 1, 2));
    }
}
