using System.Numerics;
using Assimp;
using Sextant;
using SkiaSharp;

namespace Sextant.Git.Tests;

public class FbxPreviewTests
{
    // box.fbx and animation.fbx are assimp's test models, under the BSD-3 license.
    [Fact]
    public void A_binary_fbx_box_draws_a_still_and_a_summary()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "box.fbx"));
        var still = FbxPreview.Draw(bytes);
        Assert.Equal("", still.Error);
        Assert.NotNull(still.Png);
        Assert.Contains("vertices", still.Summary, StringComparison.Ordinal);
        Assert.Contains("triangles", still.Summary, StringComparison.Ordinal);
        Assert.Contains("Materials:", still.Summary, StringComparison.Ordinal);
        Assert.Contains("Animations:", still.Summary, StringComparison.Ordinal);
        using var bitmap = SKBitmap.Decode(still.Png);
        Assert.Equal(480, bitmap.Width);
        Assert.Equal(360, bitmap.Height);
        Assert.True(OpaqueCount(bitmap) > 100);
        Assert.Equal(0, bitmap.GetPixel(2, 2).Alpha);
    }

    [Fact]
    public void An_exported_fbx_reports_the_mesh_material_and_animation()
    {
        var bytes = ExportCube();
        var still = FbxPreview.Draw(bytes);
        Assert.Equal("", still.Error);
        Assert.Contains("CubeNode", still.Summary, StringComparison.Ordinal);
        Assert.Contains("Clay", still.Summary, StringComparison.Ordinal);
        Assert.Contains("Animations: none", still.Summary, StringComparison.Ordinal);
        Assert.NotNull(still.Png);
        using var bitmap = SKBitmap.Decode(still.Png);
        Assert.True(bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).Alpha > 200);
    }

    [Fact]
    public void An_animated_fbx_names_its_take()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "animation.fbx"));
        var still = FbxPreview.Draw(bytes);
        Assert.Equal("", still.Error);
        Assert.NotNull(still.Png);
        Assert.DoesNotContain("Animations: none", still.Summary, StringComparison.Ordinal);
        Assert.Contains("Animations:", still.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_that_are_not_an_fbx_explain_the_failure()
    {
        var still = FbxPreview.Draw("not an fbx"u8.ToArray());
        Assert.Null(still.Png);
        Assert.False(string.IsNullOrWhiteSpace(still.Error));
    }

    private static byte[] ExportCube()
    {
        using var context = new AssimpContext();
        var scene = new Scene();
        scene.RootNode = new Node("Root");
        var mesh = new Mesh("Cube", PrimitiveType.Triangle);
        mesh.Vertices.Add(new Vector3(-1, -1, -1));
        mesh.Vertices.Add(new Vector3(1, -1, -1));
        mesh.Vertices.Add(new Vector3(1, 1, -1));
        mesh.Vertices.Add(new Vector3(-1, 1, -1));
        mesh.Vertices.Add(new Vector3(-1, -1, 1));
        mesh.Vertices.Add(new Vector3(1, -1, 1));
        mesh.Vertices.Add(new Vector3(1, 1, 1));
        mesh.Vertices.Add(new Vector3(-1, 1, 1));
        AddFace(mesh, 0, 1, 2);
        AddFace(mesh, 0, 2, 3);
        AddFace(mesh, 4, 6, 5);
        AddFace(mesh, 4, 7, 6);
        AddFace(mesh, 0, 4, 5);
        AddFace(mesh, 0, 5, 1);
        AddFace(mesh, 1, 5, 6);
        AddFace(mesh, 1, 6, 2);
        AddFace(mesh, 2, 6, 7);
        AddFace(mesh, 2, 7, 3);
        AddFace(mesh, 3, 7, 4);
        AddFace(mesh, 3, 4, 0);
        mesh.MaterialIndex = 0;
        scene.Meshes.Add(mesh);
        var material = new Material { Name = "Clay" };
        scene.Materials.Add(material);
        var node = new Node("CubeNode");
        node.MeshIndices.Add(0);
        scene.RootNode.Children.Add(node);
        var format = context.GetSupportedExportFormats()
            .First(item => string.Equals(item.FormatId, "fbx", StringComparison.OrdinalIgnoreCase)
                || item.FileExtension.Contains("fbx", StringComparison.OrdinalIgnoreCase));
        var blob = context.ExportToBlob(scene, format.FormatId);
        Assert.NotNull(blob);
        Assert.True(blob.HasData);
        return blob.Data;
    }

    private static void AddFace(Mesh mesh, int a, int b, int c) => mesh.Faces.Add(new Face([a, b, c]));

    private static int OpaqueCount(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (bitmap.GetPixel(x, y).Alpha > 200)
                    count++;
            }
        }

        return count;
    }
}
