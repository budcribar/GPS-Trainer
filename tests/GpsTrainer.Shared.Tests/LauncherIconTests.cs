using System.IO.Compression;

namespace GpsTrainer.Shared.Tests;

public class LauncherIconTests
{
    [Fact]
    public void Adaptive_icon_declares_separate_background_foreground_and_monochrome()
    {
        var xml = File.ReadAllText(RepoFile("src/GpsTrainer.App/Platforms/Android/Resources/mipmap-anydpi-v26/appicon.xml"));
        var round = File.ReadAllText(RepoFile("src/GpsTrainer.App/Platforms/Android/Resources/mipmap-anydpi-v26/appicon_round.xml"));

        Assert.Contains("@color/appicon_background", xml);
        Assert.Contains("@mipmap/appicon_foreground", xml);
        Assert.Contains("@drawable/appicon_monochrome", xml);
        Assert.Equal(xml, round);
    }

    [Fact]
    public void Foreground_mark_stays_inside_the_66dp_safe_circle()
    {
        var png = ReadRgba(RepoFile("src/GpsTrainer.App/Platforms/Android/Resources/mipmap-xxxhdpi/appicon_foreground.png"));
        var radius = png.Width * (66.0 / 108.0) / 2.0;
        var center = (png.Width - 1) / 2.0;
        var farthest = 0.0;
        for (var y = 0; y < png.Height; y++)
        {
            for (var x = 0; x < png.Width; x++)
            {
                if (png.Alpha(x, y) < 200)
                    continue;
                var dx = x - center;
                var dy = y - center;
                farthest = Math.Max(farthest, Math.Sqrt(dx * dx + dy * dy));
            }
        }

        Assert.True(farthest > radius * 0.4, "The mark should still be large enough to see.");
        Assert.True(farthest <= radius + 1.0, $"Foreground reaches {farthest:0.0}px, safe radius is {radius:0.0}px.");
    }

    [Fact]
    public void Play_icon_is_512_and_legacy_mipmaps_exist()
    {
        var play = ReadRgba(RepoFile("src/GpsTrainer.App/Resources/AppIcon/icon-512.png"));
        Assert.Equal(512, play.Width);
        Assert.Equal(512, play.Height);
        foreach (var density in new[] { "mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi" })
        {
            Assert.True(File.Exists(RepoFile($"src/GpsTrainer.App/Platforms/Android/Resources/mipmap-{density}/appicon.png")));
            Assert.True(File.Exists(RepoFile($"src/GpsTrainer.App/Platforms/Android/Resources/mipmap-{density}/appicon_round.png")));
            Assert.True(File.Exists(RepoFile($"src/GpsTrainer.App/Platforms/Android/Resources/drawable-{density}/appicon_monochrome.png")));
        }
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    private static Rgba ReadRgba(string path)
    {
        using var stream = File.OpenRead(path);
        var signature = new byte[8];
        stream.ReadExactly(signature);
        using var chunks = new MemoryStream();
        var width = 0;
        var height = 0;
        var colorType = 0;
        var bitDepth = 0;
        var interlace = 0;
        while (stream.Position < stream.Length)
        {
            var length = ReadInt(stream);
            var type = new byte[4];
            stream.ReadExactly(type);
            var data = new byte[length];
            stream.ReadExactly(data);
            stream.ReadExactly(new byte[4]);
            var name = System.Text.Encoding.ASCII.GetString(type);
            if (name == "IHDR")
            {
                width = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
                height = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];
                bitDepth = data[8];
                colorType = data[9];
                interlace = data[12];
            }
            else if (name == "IDAT")
            {
                chunks.Write(data);
            }
            else if (name == "IEND")
            {
                break;
            }
        }

        Assert.Equal(8, bitDepth);
        Assert.Equal(6, colorType);
        Assert.Equal(0, interlace);
        chunks.Position = 0;
        using var deflate = new ZLibStream(chunks, CompressionMode.Decompress);
        var raw = new byte[height * (1 + width * 4)];
        deflate.ReadExactly(raw);
        var pixels = new byte[width * height * 4];
        var stride = width * 4;
        var previous = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var row = new byte[stride];
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, row, 0, stride);
            for (var i = 0; i < stride; i++)
            {
                var left = i >= 4 ? row[i - 4] : 0;
                var up = previous[i];
                var upLeft = i >= 4 ? previous[i - 4] : 0;
                row[i] = (byte)(row[i] + filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException("Unknown PNG filter " + filter)
                });
            }
            Buffer.BlockCopy(row, 0, pixels, y * stride, stride);
            previous = row;
        }
        return new Rgba(width, height, pixels);
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var diagonal = Math.Abs(estimate - upLeft);
        if (leftDistance <= upDistance && leftDistance <= diagonal)
            return left;
        return upDistance <= diagonal ? up : upLeft;
    }

    private static int ReadInt(Stream stream)
    {
        var bytes = new byte[4];
        stream.ReadExactly(bytes);
        return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    }

    private sealed record Rgba(int Width, int Height, byte[] Pixels)
    {
        public int Alpha(int x, int y) => Pixels[(y * Width + x) * 4 + 3];
    }
}
