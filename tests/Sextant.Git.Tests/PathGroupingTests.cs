namespace Sextant.Git.Tests;

public class PathGroupingTests
{
    [Fact]
    public void Shared_prefix_becomes_a_folder()
    {
        var nodes = PathGrouping.Group(["main", "feature/grass", "feature/figma-ui"]);

        Assert.Equal(["feature", "main"], nodes.Select(node => node.Label).ToArray());
        Assert.Null(nodes[0].RefPath);
        Assert.Equal("feature", nodes[0].FullName);
        Assert.Equal(["figma-ui", "grass"], nodes[0].Children.Select(node => node.Label).ToArray());
        Assert.Equal("feature/figma-ui", nodes[0].Children[0].RefPath);
        Assert.Equal("feature/grass", nodes[0].Children[1].RefPath);
        Assert.Equal("main", nodes[1].RefPath);
        Assert.Empty(nodes[1].Children);
    }

    [Fact]
    public void A_single_slashed_name_stays_one_row()
    {
        var nodes = PathGrouping.Group(["feature/grass"]);

        var only = Assert.Single(nodes);
        Assert.Equal("feature/grass", only.Label);
        Assert.Equal("feature/grass", only.RefPath);
        Assert.Empty(only.Children);
    }

    [Fact]
    public void A_branch_with_the_folder_name_keeps_that_ref_and_its_children()
    {
        var nodes = PathGrouping.Group(["feature", "feature/grass", "feature/figma-ui"]);

        var feature = Assert.Single(nodes);
        Assert.Equal("feature", feature.RefPath);
        Assert.Equal(["figma-ui", "grass"], feature.Children.Select(node => node.Label).ToArray());
        Assert.Equal("feature/figma-ui", feature.Children[0].RefPath);
        Assert.Equal("feature/grass", feature.Children[1].RefPath);
    }

    [Fact]
    public void Deeper_shared_prefixes_nest_and_a_lone_tail_stays_flat()
    {
        var nodes = PathGrouping.Group(["a/b/c", "a/b/d", "a/e", "a/z/only"]);

        var root = Assert.Single(nodes);
        Assert.Equal("a", root.Label);
        Assert.Null(root.RefPath);
        Assert.Equal(["b", "e", "z/only"], root.Children.Select(node => node.Label).ToArray());
        Assert.Equal(["c", "d"], root.Children[0].Children.Select(node => node.Label).ToArray());
        Assert.Equal("a/b/c", root.Children[0].Children[0].RefPath);
        Assert.Equal("a/b/d", root.Children[0].Children[1].RefPath);
        Assert.Equal("a/e", root.Children[1].RefPath);
        Assert.Equal("a/z/only", root.Children[2].RefPath);
    }

    [Fact]
    public void A_prefix_that_is_also_a_longer_name_nests_the_remainder()
    {
        var nodes = PathGrouping.Group(["release/1.0", "release/1.0/beta"]);

        var release = Assert.Single(nodes);
        Assert.Null(release.RefPath);
        var version = Assert.Single(release.Children);
        Assert.Equal("release/1.0", version.RefPath);
        var beta = Assert.Single(version.Children);
        Assert.Equal("release/1.0/beta", beta.RefPath);
        Assert.Equal("beta", beta.Label);
    }
}
