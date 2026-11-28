using System.Buffers.Binary;
using System.IO.Compression;

namespace LocalExile.ContentPatch;

/// <summary>
/// Turns a PNG into the two files the 3.29.3c client reads for a texture, as its own 2D item art is stored
/// (Art/2DItems/Currency/CurrencyRerollRare.dds, exported 2026-11-20):
/// <list type="bullet">
/// <item><c>X.dds</c>: a DX10 DDS, DXGI format 28 (R8G8B8A8_UNORM, uncompressed), with every mip level down to 1×1.</item>
/// <item><c>X.dds.header</c>: u32 3, u32 width, u32 height, u32 mip count, u32 21, u32 size of the DDS that follows,
/// u32 size of X.dds, then a legacy DDS (A8B8G8R8 masks) holding the smaller mips from the first one at most 20 pixels
/// wide (for 80×80 the real file holds 20, 10, 5, 2, 1). The word 21 is copied from the real file as is.</item>
/// </list>
/// The PNG must be 8-bit RGBA or RGB, not interlaced.
/// </summary>
public static class Texture
{
    public static (byte[] Dds, byte[] Header) FromPng(byte[] png)
    {
        var (w, h, rgba) = DecodePng(png);
        var mips = Mips(w, h, rgba);
        byte[] dds = Dx10Dds(w, h, mips);
        int first = mips.FindIndex(m => m.W <= 20);
        if (first < 0) first = mips.Count - 1;
        var small = mips.Skip(first).ToList();
        byte[] thumb = LegacyDds(small);
        var header = new byte[28 + thumb.Length];
        var s = header.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(s, 3);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], w);
        BinaryPrimitives.WriteInt32LittleEndian(s[8..], h);
        BinaryPrimitives.WriteInt32LittleEndian(s[12..], mips.Count);
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], 21);
        BinaryPrimitives.WriteInt32LittleEndian(s[20..], thumb.Length);
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], dds.Length);
        thumb.CopyTo(s[28..]);
        return (dds, header);
    }

    record struct Mip(int W, int H, byte[] Rgba);

    static List<Mip> Mips(int w, int h, byte[] rgba)
    {
        var list = new List<Mip> { new(w, h, rgba) };
        while (w > 1 || h > 1)
        {
            int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
            var next = new byte[nw * nh * 4];
            var prev = list[^1].Rgba;
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    // Box filter over the source pixels this one covers, weighted by alpha so clear pixels add no colour.
                    int x0 = x * w / nw, x1 = Math.Max(x0 + 1, (x + 1) * w / nw), y0 = y * h / nh, y1 = Math.Max(y0 + 1, (y + 1) * h / nh);
                    double r = 0, g = 0, b = 0, a = 0; int n = 0;
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = x0; xx < x1; xx++)
                        {
                            int i = (yy * w + xx) * 4; double al = prev[i + 3];
                            r += prev[i] * al; g += prev[i + 1] * al; b += prev[i + 2] * al; a += al; n++;
                        }
                    int o = (y * nw + x) * 4;
                    if (a > 0) { next[o] = (byte)Math.Round(r / a); next[o + 1] = (byte)Math.Round(g / a); next[o + 2] = (byte)Math.Round(b / a); }
                    next[o + 3] = (byte)Math.Round(a / n);
                }
            list.Add(new(nw, nh, next));
            w = nw; h = nh;
        }
        return list;
    }

    static void DdsHeader(Span<byte> s, int w, int h, int mips, bool dx10)
    {
        "DDS "u8.CopyTo(s);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], 124);
        BinaryPrimitives.WriteInt32LittleEndian(s[8..], dx10 ? 0x21007 : 0x2100f);   // caps|height|width|pixelformat|mipcount (+pitch)
        BinaryPrimitives.WriteInt32LittleEndian(s[12..], h);
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], w);
        BinaryPrimitives.WriteInt32LittleEndian(s[20..], dx10 ? 0 : w * 4);
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], 1);                              // depth, 1 in both of the real files
        BinaryPrimitives.WriteInt32LittleEndian(s[28..], mips);
        var pf = s[76..];
        BinaryPrimitives.WriteInt32LittleEndian(pf, 32);
        if (dx10)
        {
            BinaryPrimitives.WriteInt32LittleEndian(pf[4..], 4);                         // fourcc
            "DX10"u8.CopyTo(pf[8..]);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(pf[4..], 0x41);                      // rgb|alphapixels
            BinaryPrimitives.WriteInt32LittleEndian(pf[12..], 32);
            BinaryPrimitives.WriteUInt32LittleEndian(pf[16..], 0x000000ff);
            BinaryPrimitives.WriteUInt32LittleEndian(pf[20..], 0x0000ff00);
            BinaryPrimitives.WriteUInt32LittleEndian(pf[24..], 0x00ff0000);
            BinaryPrimitives.WriteUInt32LittleEndian(pf[28..], 0xff000000);
        }
        BinaryPrimitives.WriteInt32LittleEndian(s[108..], 0x401008);                     // complex|texture|mipmap
    }

    static byte[] Dx10Dds(int w, int h, List<Mip> mips)
    {
        int size = 148 + mips.Sum(m => m.Rgba.Length);
        var b = new byte[size];
        DdsHeader(b, w, h, mips.Count, dx10: true);
        var x = b.AsSpan(128);
        BinaryPrimitives.WriteInt32LittleEndian(x, 28);        // DXGI_FORMAT_R8G8B8A8_UNORM
        BinaryPrimitives.WriteInt32LittleEndian(x[4..], 3);    // texture 2D
        BinaryPrimitives.WriteInt32LittleEndian(x[8..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(x[12..], 1);   // array size
        BinaryPrimitives.WriteInt32LittleEndian(x[16..], 0);
        int at = 148;
        foreach (var m in mips) { m.Rgba.CopyTo(b, at); at += m.Rgba.Length; }
        return b;
    }

    static byte[] LegacyDds(List<Mip> mips)
    {
        var b = new byte[128 + mips.Sum(m => m.Rgba.Length)];
        DdsHeader(b, mips[0].W, mips[0].H, mips.Count, dx10: false);
        int at = 128;
        foreach (var m in mips) { m.Rgba.CopyTo(b, at); at += m.Rgba.Length; }
        return b;
    }

    public static (int W, int H, byte[] Rgba) DecodePng(byte[] png)
    {
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            throw new InvalidDataException("not a PNG");
        int w = 0, h = 0, colour = 0;
        using var idat = new MemoryStream();
        int at = 8;
        while (at + 8 <= png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, len);
            if (type == "IHDR")
            {
                w = BinaryPrimitives.ReadInt32BigEndian(data); h = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                int depth = data[8]; colour = data[9];
                if (depth != 8 || (colour != 6 && colour != 2) || data[12] != 0)
                    throw new InvalidDataException("PNG must be 8-bit RGBA or RGB and not interlaced");
            }
            else if (type == "IDAT") idat.Write(data);
            else if (type == "IEND") break;
            at += 12 + len;
        }
        int bpp = colour == 6 ? 4 : 3, stride = w * bpp;
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var src = raw.ToArray();
        var pix = new byte[h * stride];
        for (int y = 0; y < h; y++)
        {
            int filter = src[y * (stride + 1)];
            var line = src.AsSpan(y * (stride + 1) + 1, stride);
            var cur = pix.AsSpan(y * stride, stride);
            var up = y > 0 ? pix.AsSpan((y - 1) * stride, stride) : new byte[stride];
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0, b = up[i], c = i >= bpp ? up[i - bpp] : 0;
                int p = filter switch
                {
                    0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException($"PNG filter {filter}"),
                };
                cur[i] = (byte)(line[i] + p);
            }
        }
        if (colour == 6) return (w, h, pix);
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { rgba[i * 4] = pix[i * 3]; rgba[i * 4 + 1] = pix[i * 3 + 1]; rgba[i * 4 + 2] = pix[i * 3 + 2]; rgba[i * 4 + 3] = 255; }
        return (w, h, rgba);
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
