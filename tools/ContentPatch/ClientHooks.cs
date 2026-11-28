using System.Buffers.Binary;
using System.Text;

namespace LocalExile.ContentPatch;

/// <summary>
/// Code hooks in the patched client executable (Client/Game.patched.exe, the copy LocalExile starts), for what data
/// alone cannot do. One so far: <c>itemSymbols</c>, a symbol of a mod's own beside the name of an item that has a given
/// modifier among its enchantments (tooltip header and ground label), drawn in place of the synthesised/fractured one.
/// <para>The code (hooks/item_symbols.py → ItemSymbolsCode.cs) goes into a section of its own, ".lxhook", added at the
/// end of the image; three jumps in the client's code lead to it. The section also holds the table of modifier Ids and
/// sprite names, rewritten at every apply (an empty table leaves the client as it was, apart from the jumps).</para>
/// <para>The executable's signature is dropped when the section is added (LocalExile's own patch already breaks it).</para>
/// </summary>
public static class ClientHooks
{
    public sealed record ItemSymbol(string Mod, string Sprite);

    const string SectionName = ".lxhook";

    /// <summary>Writes the table (and, once, the section and the jumps). Returns a line for the log.</summary>
    public static string Apply(string exePath, IReadOnlyList<ItemSymbol> symbols)
    {
        if (!File.Exists(exePath)) return $"no {exePath}: item symbols not installed (patch the client first)";
        byte[] exe = File.ReadAllBytes(exePath);
        var pe = Pe.Parse(exe);
        int section = pe.FindSection(SectionName);
        bool added = false;
        if (section < 0)
        {
            if (symbols.Count == 0) return "no item symbols wanted, the client has no hook section: nothing to do";
            foreach (var (site, original) in Sites)
                if (!exe.AsSpan(pe.FileOffset(site), original.Length).SequenceEqual(original))
                    return $"item symbols not installed: the client's code at 0x{site:X} is not what the hook expects (another build?)";
            exe = pe.AddSection(exe, SectionName, ItemSymbolsCode.Size, out int rva);
            pe = Pe.Parse(exe);
            section = pe.FindSection(SectionName);
            int offset = pe.Sections[section].RawPointer;
            ItemSymbolsCode.Code.CopyTo(exe, offset);
            // The code was assembled for SectionRva: its jumps and calls into the client move by the difference.
            int delta = ItemSymbolsCode.SectionRva - rva;
            foreach (int field in ItemSymbolsCode.Relocations)
            {
                int at = offset + field;
                BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(at), BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(at)) + delta);
            }
            long sectionVa = pe.ImageBase + rva;
            Jump(exe, pe, ItemSymbolsCode.BuilderSite, sectionVa + ItemSymbolsCode.Builder, ItemSymbolsCode.BuilderOriginal.Length);
            Jump(exe, pe, ItemSymbolsCode.TooltipSite, sectionVa + ItemSymbolsCode.Tooltip, ItemSymbolsCode.TooltipOriginal.Length);
            Jump(exe, pe, ItemSymbolsCode.LabelSite, sectionVa + ItemSymbolsCode.Label, ItemSymbolsCode.LabelOriginal.Length);
            added = true;
        }
        var s = pe.Sections[section];
        byte[] table = Table(symbols);
        var place = exe.AsSpan(s.RawPointer + ItemSymbolsCode.Table, ItemSymbolsCode.Size - ItemSymbolsCode.Table);
        if (!added && place[..table.Length].SequenceEqual(table) && place[table.Length..].IndexOfAnyExcept((byte)0) < 0)
            return $"item symbols unchanged ({symbols.Count})";
        place.Clear();
        table.CopyTo(place);
        File.WriteAllBytes(exePath, exe);
        return $"item symbols: {symbols.Count} ({string.Join(", ", symbols.Select(x => x.Mod))}){(added ? "; hook section added to " + exePath : "")}";
    }

    static IEnumerable<(long Site, byte[] Original)> Sites =>
    [
        (ItemSymbolsCode.BuilderSite, ItemSymbolsCode.BuilderOriginal),
        (ItemSymbolsCode.TooltipSite, ItemSymbolsCode.TooltipOriginal),
        (ItemSymbolsCode.LabelSite, ItemSymbolsCode.LabelOriginal),
    ];

    // The table at Table (u32 count, count × {u32 Id offset, u32 sprite offset}) and its strings at Strings, both from
    // the section's start; laid out from Table so it is written in one piece.
    static byte[] Table(IReadOnlyList<ItemSymbol> symbols)
    {
        int stringsAt = ItemSymbolsCode.Strings - ItemSymbolsCode.Table;
        if (4 + symbols.Count * 8 > stringsAt) throw new InvalidDataException($"at most {(stringsAt - 4) / 8} item symbols");
        using var strings = new MemoryStream();
        var head = new byte[stringsAt];
        BinaryPrimitives.WriteInt32LittleEndian(head, symbols.Count);
        int String(string text)
        {
            int at = ItemSymbolsCode.Strings + (int)strings.Length;
            strings.Write(Encoding.Unicode.GetBytes(text + "\0"));
            return at;
        }
        for (int i = 0; i < symbols.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(4 + i * 8), String(symbols[i].Mod));
            BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(8 + i * 8), String(symbols[i].Sprite));
        }
        if (ItemSymbolsCode.Strings + strings.Length > ItemSymbolsCode.Size) throw new InvalidDataException("item symbol names too long");
        return [.. head, .. strings.ToArray()];
    }

    static void Jump(byte[] exe, Pe pe, long site, long target, int length)
    {
        int at = pe.FileOffset(site);
        exe[at] = 0xE9;
        BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(at + 1), checked((int)(target - (site + 5))));
        for (int i = 5; i < length; i++) exe[at + i] = 0x90;
    }

    /// <summary>Just enough of the PE format: the section table, and adding a section at the end.</summary>
    sealed class Pe
    {
        public sealed record Section(string Name, int VirtualAddress, int VirtualSize, int RawPointer, int RawSize, int HeaderAt);
        public long ImageBase;
        public int SectionAlignment, FileAlignment, SizeOfImage, SizeOfHeaders, OptionalAt, SectionsAt;
        public List<Section> Sections = [];

        public static Pe Parse(byte[] b)
        {
            int pe = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x3C));
            if (BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pe)) != 0x4550) throw new InvalidDataException("not a PE file");
            int count = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pe + 6));
            int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pe + 20));
            int opt = pe + 24;
            if (BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(opt)) != 0x20B) throw new InvalidDataException("not a 64-bit image");
            var p = new Pe
            {
                OptionalAt = opt,
                ImageBase = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(opt + 24)),
                SectionAlignment = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(opt + 32)),
                FileAlignment = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(opt + 36)),
                SizeOfImage = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(opt + 56)),
                SizeOfHeaders = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(opt + 60)),
                SectionsAt = opt + optionalSize,
            };
            for (int i = 0; i < count; i++)
            {
                int h = p.SectionsAt + i * 40;
                p.Sections.Add(new(Encoding.ASCII.GetString(b, h, 8).TrimEnd('\0'),
                    BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(h + 12)), BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(h + 8)),
                    BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(h + 20)), BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(h + 16)), h));
            }
            return p;
        }

        public int FindSection(string name) => Sections.FindIndex(s => s.Name == name);

        public int FileOffset(long va)
        {
            long rva = va - ImageBase;
            foreach (var s in Sections)
                if (rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.RawSize))
                    return checked((int)(rva - s.VirtualAddress + s.RawPointer));
            throw new InvalidDataException($"0x{va:X} is in no section");
        }

        static int Align(int v, int a) => (v + a - 1) / a * a;

        /// <summary>A copy of the image with an empty RWX section of <paramref name="size"/> bytes at its end.</summary>
        public byte[] AddSection(byte[] b, string name, int size, out int rva)
        {
            int header = SectionsAt + Sections.Count * 40;
            if (header + 40 > SizeOfHeaders || Sections.Min(s => s.RawPointer) < header + 40)
                throw new InvalidDataException("no room for another section header");
            // The signature (certificate table, data directory 4) sits after the sections' raw data: drop it.
            int security = OptionalAt + 112 + 4 * 8;
            int certAt = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(security));
            int end = Sections.Max(s => s.RawPointer + s.RawSize);
            int length = certAt > 0 && certAt >= end ? certAt : b.Length;
            int raw = Align(Math.Max(end, length), FileAlignment);
            rva = Align(Sections.Max(s => s.VirtualAddress + Math.Max(s.VirtualSize, s.RawSize)), SectionAlignment);
            int rawSize = Align(size, FileAlignment);
            var copy = new byte[raw + rawSize];
            b.AsSpan(0, Math.Min(length, b.Length)).CopyTo(copy);
            var h = copy.AsSpan(header, 40);
            h.Clear();
            Encoding.ASCII.GetBytes(name).CopyTo(h);
            BinaryPrimitives.WriteInt32LittleEndian(h[8..], size);
            BinaryPrimitives.WriteInt32LittleEndian(h[12..], rva);
            BinaryPrimitives.WriteInt32LittleEndian(h[16..], rawSize);
            BinaryPrimitives.WriteInt32LittleEndian(h[20..], raw);
            BinaryPrimitives.WriteUInt32LittleEndian(h[36..], 0xE0000020);   // code, execute, read, write
            int pe = BinaryPrimitives.ReadInt32LittleEndian(copy.AsSpan(0x3C));
            BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(pe + 6), (ushort)(Sections.Count + 1));
            BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(OptionalAt + 56), Align(rva + size, SectionAlignment));
            BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(OptionalAt + 64), 0);                 // checksum
            copy.AsSpan(security, 8).Clear();
            return copy;
        }
    }
}
