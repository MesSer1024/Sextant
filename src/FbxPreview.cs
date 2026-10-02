using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Assimp;
using SkiaSharp;

namespace Sextant;

/// <summary>
/// Draws one fitted still of an FBX file and a short scene summary.
/// The picture is a clay render from a fixed three-quarter view. Material names are listed, not shaded.
/// </summary>
public static class FbxPreview
{
    private const int Width = 480;
    private const int Height = 360;
    private const int MaxDraw = 200_000;
    private const int MaxNames = 6;

    public readonly record struct FbxStill(byte[]? Png, string Summary, string Error);

    public static FbxStill Draw(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return new FbxStill(null, "", "This FBX could not be read.");
        try
        {
            using var context = new AssimpContext();
            Scene scene;
            using (var stream = new MemoryStream(data, writable: false))
                scene = context.ImportFileFromStream(stream, PostProcessSteps.Triangulate, "fbx");
            if (scene?.RootNode is null)
                return new FbxStill(null, "", "This FBX could not be read.");

            var model = Collect(scene);
            var summary = Summarize(model);
            if (model.Drawn.Count == 0)
            {
                var reason = model.Triangles == 0
                    ? "This FBX has no triangles to draw."
                    : "This FBX has no area to draw.";
                return new FbxStill(null, summary, reason);
            }

            var png = Rasterize(model.Drawn);
            if (png is null)
                return new FbxStill(null, summary, "This FBX has no area to draw.");
            return new FbxStill(png, summary, "");
        }
        catch (Exception exception)
        {
            return new FbxStill(null, "", Clean(exception.Message));
        }
    }

    private sealed class Model
    {
        public int Meshes { get; set; }

        public int Vertices { get; set; }

        public int Triangles { get; set; }

        public List<string> MeshNames { get; } = [];

        public List<string> Materials { get; } = [];

        public List<string> Animations { get; } = [];

        public int SampleStride { get; set; } = 1;

        public List<Tri> Drawn { get; } = [];
    }

    private readonly record struct Tri(Vector3 A, Vector3 B, Vector3 C, Vector3 Normal);

    private static Model Collect(Scene scene)
    {
        var model = new Model();
        if (scene.RootNode is { } root)
            NoteTree(scene, model, root);
        foreach (var animation in scene.Animations)
            model.Animations.Add(AnimationLabel(animation));

        var total = 0;
        if (scene.RootNode is { } counted)
            Walk(scene, counted, Matrix4x4.Identity, _ => total++);
        var stride = Math.Max(1, (total + MaxDraw - 1) / MaxDraw);
        model.SampleStride = stride;
        var index = 0;
        if (scene.RootNode is { } drawn)
        {
            Walk(scene, drawn, Matrix4x4.Identity, tri =>
            {
                if (index++ % stride == 0)
                    model.Drawn.Add(tri);
            });
        }

        return model;
    }

    private static void NoteTree(Scene scene, Model model, Node node)
    {
        foreach (var meshIndex in node.MeshIndices)
        {
            if (meshIndex < 0 || meshIndex >= scene.MeshCount)
                continue;
            NoteMesh(scene, model, node, scene.Meshes[meshIndex]);
        }

        foreach (var child in node.Children)
            NoteTree(scene, model, child);
    }

    private static void NoteMesh(Scene scene, Model model, Node node, Mesh mesh)
    {
        model.Meshes++;
        model.Vertices += mesh.VertexCount;
        var triangles = 0;
        foreach (var face in mesh.Faces)
        {
            if (face.IndexCount >= 3)
                triangles += face.IndexCount - 2;
        }

        model.Triangles += triangles;
        if (model.MeshNames.Count < MaxNames)
            model.MeshNames.Add(Clip(node.Name.Length > 0 ? node.Name : mesh.Name));
        if (mesh.MaterialIndex < 0 || mesh.MaterialIndex >= scene.MaterialCount)
            return;
        var material = Clip(scene.Materials[mesh.MaterialIndex].Name);
        if (!model.Materials.Contains(material))
            model.Materials.Add(material);
    }

    private static void Walk(Scene scene, Node node, Matrix4x4 parent, Action<Tri> emit)
    {
        var world = node.Transform * parent;
        foreach (var meshIndex in node.MeshIndices)
        {
            if (meshIndex < 0 || meshIndex >= scene.MeshCount)
                continue;
            var mesh = scene.Meshes[meshIndex];
            foreach (var face in mesh.Faces)
            {
                if (face.IndexCount < 3)
                    continue;
                var origin = Vertex(mesh, face.Indices[0], world);
                if (origin is null)
                    continue;
                for (var i = 1; i < face.IndexCount - 1; i++)
                {
                    var b = Vertex(mesh, face.Indices[i], world);
                    var c = Vertex(mesh, face.Indices[i + 1], world);
                    if (b is null || c is null)
                        continue;
                    var normal = Vector3.Cross(b.Value - origin.Value, c.Value - origin.Value);
                    if (normal.LengthSquared() < 1e-12f)
                        continue;
                    emit(new Tri(origin.Value, b.Value, c.Value, Vector3.Normalize(normal)));
                }
            }
        }

        foreach (var child in node.Children)
            Walk(scene, child, world, emit);
    }

    private static Vector3? Vertex(Mesh mesh, int index, Matrix4x4 world)
    {
        if (index < 0 || index >= mesh.VertexCount)
            return null;
        return Vector3.Transform(mesh.Vertices[index], world);
    }

    private static string Summarize(Model model)
    {
        var text = new StringBuilder();
        var meshWord = model.Meshes == 1 ? "mesh" : "meshes";
        text.Append(model.Meshes.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(meshWord).Append(", ");
        text.Append(model.Vertices.ToString(CultureInfo.InvariantCulture)).Append(" vertices, ");
        text.Append(model.Triangles.ToString(CultureInfo.InvariantCulture)).Append(" triangles");
        if (model.MeshNames.Count > 0)
        {
            text.AppendLine();
            text.Append(string.Join(", ", model.MeshNames));
            var extra = model.Meshes - model.MeshNames.Count;
            if (extra > 0)
                text.Append(", and ").Append(extra.ToString(CultureInfo.InvariantCulture)).Append(" more");
        }

        text.AppendLine();
        text.Append("Materials: ");
        text.Append(model.Materials.Count == 0 ? "none" : JoinLimited(model.Materials));
        text.AppendLine();
        text.Append("Animations: ");
        text.Append(model.Animations.Count == 0 ? "none" : JoinLimited(model.Animations));
        if (model.SampleStride > 1 && model.Drawn.Count > 0)
        {
            text.AppendLine();
            text.Append("The still draws ");
            text.Append(model.Drawn.Count.ToString(CultureInfo.InvariantCulture));
            text.Append(" of ");
            text.Append(model.Triangles.ToString(CultureInfo.InvariantCulture));
            text.Append(" triangles.");
        }

        return text.ToString();
    }

    private static string JoinLimited(List<string> names)
    {
        if (names.Count <= MaxNames)
            return string.Join(", ", names);
        return string.Join(", ", names.Take(MaxNames)) + ", and " + (names.Count - MaxNames).ToString(CultureInfo.InvariantCulture) + " more";
    }

    private static string AnimationLabel(Animation animation)
    {
        var name = Clip(animation.Name);
        if (animation.TicksPerSecond <= 0)
            return name;
        var seconds = animation.DurationInTicks / animation.TicksPerSecond;
        return name + " " + seconds.ToString("0.#", CultureInfo.InvariantCulture) + " s";
    }

    private static string Clip(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Mesh";
        var trimmed = name.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return trimmed.Length <= 40 ? trimmed : trimmed[..40];
    }

    private static byte[]? Rasterize(List<Tri> triangles)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var tri in triangles)
        {
            Include(ref min, ref max, tri.A);
            Include(ref min, ref max, tri.B);
            Include(ref min, ref max, tri.C);
        }

        var size = max - min;
        var extent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        if (extent < 1e-6f || float.IsInfinity(extent))
            return null;

        var center = (min + max) * 0.5f;
        var eye = center + Vector3.Normalize(new Vector3(1f, 0.8f, 1f)) * (extent * 3f);
        var view = Matrix4x4.CreateLookAt(eye, center, Vector3.UnitY);
        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? min.X : max.X,
                (i & 2) == 0 ? min.Y : max.Y,
                (i & 4) == 0 ? min.Z : max.Z);
            var projected = Vector3.Transform(corner, view);
            minX = MathF.Min(minX, projected.X);
            minY = MathF.Min(minY, projected.Y);
            maxX = MathF.Max(maxX, projected.X);
            maxY = MathF.Max(maxY, projected.Y);
        }

        var spanX = maxX - minX;
        var spanY = maxY - minY;
        if (spanX < 1e-6f || spanY < 1e-6f)
            return null;
        var scale = MathF.Min(Width * 0.84f / spanX, Height * 0.84f / spanY);
        var midX = (minX + maxX) * 0.5f;
        var midY = (minY + maxY) * 0.5f;
        var light = Vector3.Normalize(eye - center);
        var color = new byte[Width * Height * 4];
        var depth = new float[Width * Height];
        Array.Fill(depth, float.PositiveInfinity);

        foreach (var tri in triangles)
        {
            var a = Project(tri.A, view, midX, midY, scale);
            var b = Project(tri.B, view, midX, midY, scale);
            var c = Project(tri.C, view, midX, midY, scale);
            var shade = 0.25f + 0.75f * MathF.Abs(Vector3.Dot(tri.Normal, light));
            var red = (byte)Math.Clamp((int)(186 * shade), 0, 255);
            var green = (byte)Math.Clamp((int)(196 * shade), 0, 255);
            var blue = (byte)Math.Clamp((int)(208 * shade), 0, 255);
            Fill(color, depth, a, b, c, red, green, blue);
        }

        return Encode(color);
    }

    private static void Include(ref Vector3 min, ref Vector3 max, Vector3 point)
    {
        min = Vector3.Min(min, point);
        max = Vector3.Max(max, point);
    }

    private readonly record struct Screen(float X, float Y, float Z);

    private static Screen Project(Vector3 point, Matrix4x4 view, float midX, float midY, float scale)
    {
        var projected = Vector3.Transform(point, view);
        return new Screen(
            (projected.X - midX) * scale + Width * 0.5f,
            Height * 0.5f - (projected.Y - midY) * scale,
            projected.Z);
    }

    private static void Fill(byte[] color, float[] depth, Screen a, Screen b, Screen c, byte red, byte green, byte blue)
    {
        var minX = (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)));
        var maxX = (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X)));
        var minY = (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)));
        var maxY = (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y)));
        minX = Math.Clamp(minX, 0, Width - 1);
        maxX = Math.Clamp(maxX, 0, Width - 1);
        minY = Math.Clamp(minY, 0, Height - 1);
        maxY = Math.Clamp(maxY, 0, Height - 1);
        var denom = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (MathF.Abs(denom) < 1e-4f)
            return;

        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var w0 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / denom;
                var w1 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / denom;
                var w2 = 1f - w0 - w1;
                if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f)
                    continue;
                var z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
                var pixel = y * Width + x;
                if (z >= depth[pixel])
                    continue;
                depth[pixel] = z;
                var offset = pixel * 4;
                color[offset] = red;
                color[offset + 1] = green;
                color[offset + 2] = blue;
                color[offset + 3] = 255;
            }
        }
    }

    private static byte[]? Encode(byte[] rgba)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 90);
        return encoded?.ToArray();
    }

    private static string Clean(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "This FBX could not be read.";
        var trimmed = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return trimmed.Length <= 240 ? trimmed : trimmed[..240];
    }
}
