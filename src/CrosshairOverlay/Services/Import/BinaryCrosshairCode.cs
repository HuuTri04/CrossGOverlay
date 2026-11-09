using System.IO;
using System.Text;
using System.Windows.Media;
using CrosshairOverlay.Core.Models;

namespace CrosshairOverlay.Services.Import;

/// <summary>Version 1, lossless binary payload for a drawn CrosshairProfile.</summary>
internal static class BinaryCrosshairCode
{
    private const byte Version = 1;
    private static readonly LineLayerSettings InnerDefaults = LineLayerSettings.CreateInner();
    private static readonly LineLayerSettings OuterDefaults = LineLayerSettings.CreateOuter();

    public static byte[] Encode(CrosshairProfile profile)
    {
        using var stream = new MemoryStream(128);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Version);
        writer.Write(checked((byte)profile.Shape));
        var flags = (byte)((profile.IsFavorite ? 1 : 0) |
            (profile.Color != Colors.Lime ? 2 : 0) |
            (profile.SchemaVersion != CrosshairProfile.CurrentSchemaVersion ? 4 : 0));
        writer.Write(flags);
        if ((flags & 4) != 0) writer.Write(profile.SchemaVersion);
        if ((flags & 2) != 0) WriteColor(writer, profile.Color);
        writer.Write(profile.Name ?? string.Empty);

        WriteNumbers(writer,
            stackalloc double[] { profile.Opacity, profile.Scale, profile.Rotation, profile.OffsetX, profile.OffsetY },
            stackalloc double[] { 1, 1, 0, 0, 0 });
        WriteLine(writer, profile.InnerLines, InnerDefaults);
        WriteLine(writer, profile.OuterLines, OuterDefaults);
        WriteDot(writer, profile.CenterDot);
        WriteOutline(writer, profile.Outline);
        WriteRing(writer, profile.Ring);
        WriteShadow(writer, profile.Shadow);
        WriteImage(writer, profile.Image);

        writer.Flush();
        if (stream.Length > AppCrosshairCode.MaxJsonBytes)
            throw new ArgumentException("Crosshair share payload exceeds the size limit.", nameof(profile));
        return stream.ToArray();
    }

    public static CrosshairProfile Decode(byte[] payload)
    {
        using var stream = new MemoryStream(payload, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadByte() != Version) throw new InvalidDataException("Unsupported crosshair binary version.");
        var shape = reader.ReadByte();
        if (shape > (byte)CrosshairShape.XShape) throw new InvalidDataException("Invalid crosshair shape.");
        var flags = ReadFlags(reader, 0x07);
        var profile = new CrosshairProfile
        {
            Shape = (CrosshairShape)shape,
            IsFavorite = (flags & 1) != 0,
            SchemaVersion = (flags & 4) != 0 ? reader.ReadInt32() : CrosshairProfile.CurrentSchemaVersion,
            Color = (flags & 2) != 0 ? ReadColor(reader) : Colors.Lime,
            Name = reader.ReadString(),
        };

        Span<double> main = stackalloc double[] { 1, 1, 0, 0, 0 };
        ReadNumbers(reader, main);
        profile.Opacity = main[0];
        profile.Scale = main[1];
        profile.Rotation = main[2];
        profile.OffsetX = main[3];
        profile.OffsetY = main[4];
        profile.InnerLines = ReadLine(reader, InnerDefaults);
        profile.OuterLines = ReadLine(reader, OuterDefaults);
        profile.CenterDot = ReadDot(reader);
        profile.Outline = ReadOutline(reader);
        profile.Ring = ReadRing(reader);
        profile.Shadow = ReadShadow(reader);
        profile.Image = ReadImage(reader);
        if (stream.Position != stream.Length) throw new InvalidDataException("Trailing crosshair binary data.");
        return profile;
    }

    private static void WriteLine(BinaryWriter writer, LineLayerSettings line, LineLayerSettings defaults)
    {
        writer.Write((byte)((line.Enabled ? 1 : 0) |
            (line.SeparateVerticalLength ? 2 : 0) |
            (line.SeparateVerticalThickness ? 4 : 0) |
            (line.ShowTop ? 8 : 0) |
            (line.ShowBottom ? 16 : 0) |
            (line.ShowLeft ? 32 : 0) |
            (line.ShowRight ? 64 : 0) |
            (line.RoundedCaps ? 128 : 0)));
        WriteNumbers(writer,
            stackalloc double[] { line.Opacity, line.Length, line.VerticalLength, line.Thickness,
                line.VerticalThickness, line.Offset },
            stackalloc double[] { defaults.Opacity, defaults.Length, defaults.VerticalLength,
                defaults.Thickness, defaults.VerticalThickness, defaults.Offset });
    }

    private static LineLayerSettings ReadLine(BinaryReader reader, LineLayerSettings defaults)
    {
        var flags = reader.ReadByte();
        Span<double> values = stackalloc double[] { defaults.Opacity, defaults.Length,
            defaults.VerticalLength, defaults.Thickness, defaults.VerticalThickness, defaults.Offset };
        ReadNumbers(reader, values);
        return new LineLayerSettings
        {
            Enabled = (flags & 1) != 0,
            SeparateVerticalLength = (flags & 2) != 0,
            SeparateVerticalThickness = (flags & 4) != 0,
            ShowTop = (flags & 8) != 0,
            ShowBottom = (flags & 16) != 0,
            ShowLeft = (flags & 32) != 0,
            ShowRight = (flags & 64) != 0,
            RoundedCaps = (flags & 128) != 0,
            Opacity = values[0], Length = values[1], VerticalLength = values[2],
            Thickness = values[3], VerticalThickness = values[4], Offset = values[5],
        };
    }

    private static void WriteDot(BinaryWriter writer, CenterDotSettings dot)
    {
        var flags = (byte)((dot.Enabled ? 1 : 0) | (dot.UseProfileColor ? 2 : 0) |
            (dot.Color != Colors.Lime ? 4 : 0));
        writer.Write(flags);
        if ((flags & 4) != 0) WriteColor(writer, dot.Color);
        WriteNumbers(writer, stackalloc double[] { dot.Size, dot.Opacity }, stackalloc double[] { 3, 1 });
    }

    private static CenterDotSettings ReadDot(BinaryReader reader)
    {
        var flags = ReadFlags(reader, 0x07);
        var color = (flags & 4) != 0 ? ReadColor(reader) : Colors.Lime;
        Span<double> values = stackalloc double[] { 3, 1 };
        ReadNumbers(reader, values);
        return new CenterDotSettings
        {
            Enabled = (flags & 1) != 0, UseProfileColor = (flags & 2) != 0,
            Color = color, Size = values[0], Opacity = values[1],
        };
    }

    private static void WriteOutline(BinaryWriter writer, OutlineSettings outline)
    {
        var flags = (byte)((outline.Enabled ? 1 : 0) | (outline.Color != Colors.Black ? 2 : 0));
        writer.Write(flags);
        if ((flags & 2) != 0) WriteColor(writer, outline.Color);
        WriteNumbers(writer, stackalloc double[] { outline.Thickness, outline.Opacity },
            stackalloc double[] { 1, 1 });
    }

    private static OutlineSettings ReadOutline(BinaryReader reader)
    {
        var flags = ReadFlags(reader, 0x03);
        var color = (flags & 2) != 0 ? ReadColor(reader) : Colors.Black;
        Span<double> values = stackalloc double[] { 1, 1 };
        ReadNumbers(reader, values);
        return new OutlineSettings
        {
            Enabled = (flags & 1) != 0, Color = color,
            Thickness = values[0], Opacity = values[1],
        };
    }

    private static void WriteRing(BinaryWriter writer, RingSettings ring)
    {
        writer.Write((byte)((ring.Enabled ? 1 : 0) | (ring.Filled ? 2 : 0)));
        WriteNumbers(writer, stackalloc double[] { ring.Radius, ring.Thickness },
            stackalloc double[] { 12, 2 });
    }

    private static RingSettings ReadRing(BinaryReader reader)
    {
        var flags = ReadFlags(reader, 0x03);
        Span<double> values = stackalloc double[] { 12, 2 };
        ReadNumbers(reader, values);
        return new RingSettings
        {
            Enabled = (flags & 1) != 0, Filled = (flags & 2) != 0,
            Radius = values[0], Thickness = values[1],
        };
    }

    private static void WriteShadow(BinaryWriter writer, CrosshairShadowSettings shadow)
    {
        var flags = (byte)((shadow.Enabled ? 1 : 0) | (shadow.Color != Colors.Black ? 2 : 0));
        writer.Write(flags);
        if ((flags & 2) != 0) WriteColor(writer, shadow.Color);
        WriteNumbers(writer, stackalloc double[] { shadow.BlurRadius, shadow.Opacity },
            stackalloc double[] { 4, 0.7 });
    }

    private static CrosshairShadowSettings ReadShadow(BinaryReader reader)
    {
        var flags = ReadFlags(reader, 0x03);
        var color = (flags & 2) != 0 ? ReadColor(reader) : Colors.Black;
        Span<double> values = stackalloc double[] { 4, 0.7 };
        ReadNumbers(reader, values);
        return new CrosshairShadowSettings
        {
            Enabled = (flags & 1) != 0, Color = color,
            BlurRadius = values[0], Opacity = values[1],
        };
    }

    private static void WriteImage(BinaryWriter writer, CustomImageSettings image)
    {
        writer.Write((byte)(image.FilePath is null ? 0 : 1));
        if (image.FilePath is not null) writer.Write(image.FilePath);
        WriteNumbers(writer,
            stackalloc double[] { image.Scale, image.Opacity, image.OffsetX, image.OffsetY },
            stackalloc double[] { 1, 1, 0, 0 });
    }

    private static CustomImageSettings ReadImage(BinaryReader reader)
    {
        var flags = ReadFlags(reader, 0x01);
        var path = (flags & 1) != 0 ? reader.ReadString() : null;
        Span<double> values = stackalloc double[] { 1, 1, 0, 0 };
        ReadNumbers(reader, values);
        return new CustomImageSettings
        {
            FilePath = path, Scale = values[0], Opacity = values[1],
            OffsetX = values[2], OffsetY = values[3],
        };
    }

    private static void WriteNumbers(BinaryWriter writer, ReadOnlySpan<double> values, ReadOnlySpan<double> defaults)
    {
        byte mask = 0;
        for (var index = 0; index < values.Length; index++)
        {
            if (!double.IsFinite(values[index])) throw new ArgumentException("Non-finite crosshair parameter.");
            if (BitConverter.DoubleToInt64Bits(values[index]) != BitConverter.DoubleToInt64Bits(defaults[index]))
                mask |= (byte)(1 << index);
        }
        writer.Write(mask);
        for (var index = 0; index < values.Length; index++)
            if ((mask & (1 << index)) != 0) writer.Write(values[index]);
    }

    private static void ReadNumbers(BinaryReader reader, Span<double> values)
    {
        var mask = ReadFlags(reader, (1 << values.Length) - 1);
        for (var index = 0; index < values.Length; index++)
        {
            if ((mask & (1 << index)) == 0) continue;
            var value = reader.ReadDouble();
            if (!double.IsFinite(value)) throw new InvalidDataException("Non-finite crosshair parameter.");
            values[index] = value;
        }
    }

    private static byte ReadFlags(BinaryReader reader, int allowedMask)
    {
        var flags = reader.ReadByte();
        if ((flags & ~allowedMask) != 0) throw new InvalidDataException("Unknown crosshair binary flags.");
        return flags;
    }

    private static void WriteColor(BinaryWriter writer, Color color)
    {
        writer.Write(color.A);
        writer.Write(color.R);
        writer.Write(color.G);
        writer.Write(color.B);
    }

    private static Color ReadColor(BinaryReader reader) => Color.FromArgb(
        reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
}
