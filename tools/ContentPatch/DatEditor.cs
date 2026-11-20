using System.Buffers.Binary;
using System.Text;
using GameServer.Data.Dat;

namespace LocalExile.ContentPatch;

/// <summary>
/// A <c>.datc64</c> table that can take new rows and changed fields (DatFile reads the same layout): u32 row count,
/// the fixed rows, then the variable section, which starts with eight 0xBB and holds the strings (UTF-16LE, four zero
/// bytes after) and arrays the rows point into by offsets from its start.
/// <para>A new row is a copy of a template row of the same table with some fields set, so every column the mod does
/// not name keeps a value the client already reads for a real row. New strings and arrays are added at the end of the
/// variable section; nothing that was there moves, so the old rows' offsets stay right.</para>
/// </summary>
public sealed class DatEditor
{
    public const ulong NullRow = 0xFEFEFEFEFEFEFEFE;
    readonly SchemaTable _table;
    readonly List<byte[]> _rows = [];
    readonly MemoryStream _variable = new();
    readonly Dictionary<string, (int Offset, SchemaColumn Column)> _columns = new(StringComparer.Ordinal);
    public int RowLength { get; }
    public int RowCount => _rows.Count;
    public string Name => _table.Name;

    DatEditor(SchemaTable table, int rowLength)
    {
        _table = table;
        RowLength = rowLength;
        int at = 0;
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var c = table.Columns[i];
            string name = string.IsNullOrEmpty(c.Name) ? $"__col{i}" : c.Name;
            _columns.TryAdd(name, (at, c));
            at += Size(c);
        }
        if (at != rowLength)
            throw new InvalidDataException($"{table.Name}: the schema's row is {at} bytes, the client's {rowLength}: the schema is not this build's");
    }

    public static DatEditor Parse(byte[] file, SchemaTable table)
    {
        int rows = (int)BinaryPrimitives.ReadUInt32LittleEndian(file);
        int boundary = FindMagic(file, rows);
        int rowLength = rows > 0 ? boundary / rows : 0;
        var e = new DatEditor(table, rowLength);
        for (int r = 0; r < rows; r++) e._rows.Add(file.AsSpan(4 + r * rowLength, rowLength).ToArray());
        e._variable.Write(file, 4 + boundary, file.Length - 4 - boundary);
        return e;
    }

    static int FindMagic(byte[] file, int rows)
    {
        for (int at = 4; at + 8 <= file.Length; at++)
        {
            if (rows > 0 && (at - 4) % rows != 0) continue;
            bool magic = true;
            for (int k = 0; k < 8 && magic; k++) magic = file[at + k] == 0xBB;
            if (magic) return at - 4;
        }
        throw new InvalidDataException("no variable-data marker");
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(four, (uint)_rows.Count);
        ms.Write(four);
        foreach (var row in _rows) ms.Write(row);
        _variable.Position = 0;
        _variable.CopyTo(ms);
        return ms.ToArray();
    }

    static int Atom(DatColumnType t) => t switch
    {
        DatColumnType.Bool => 1,
        DatColumnType.String => 8,
        DatColumnType.I16 or DatColumnType.U16 => 2,
        DatColumnType.I32 or DatColumnType.U32 or DatColumnType.F32 or DatColumnType.EnumRow => 4,
        DatColumnType.Row => 8,
        DatColumnType.ForeignRow => 16,
        DatColumnType.Array => 16,
        _ => 1,
    };

    static int Size(SchemaColumn c) => c.IsArray ? 16 : Atom(c.Type) * (c.IsInterval ? 2 : 1);

    (int Offset, SchemaColumn Column) Column(string name) =>
        _columns.TryGetValue(name, out var c) ? c : throw new KeyNotFoundException($"{_table.Name} has no column {name}");

    public bool HasColumn(string name) => _columns.ContainsKey(name);

    // ---- reading -------------------------------------------------------------------------------------------------

    string ReadString(long offset)
    {
        var v = _variable.GetBuffer().AsSpan(0, (int)_variable.Length);
        int end = (int)offset;
        while (end + 4 <= v.Length && !(v[end] == 0 && v[end + 1] == 0 && v[end + 2] == 0 && v[end + 3] == 0)) end += 2;
        return Encoding.Unicode.GetString(v[(int)offset..end]);
    }

    public string GetString(int row, string column)
    {
        var (at, c) = Column(column);
        if (c.Type != DatColumnType.String || c.IsArray) throw new InvalidOperationException($"{_table.Name}.{column} is not a string");
        return ReadString((long)BinaryPrimitives.ReadUInt64LittleEndian(_rows[row].AsSpan(at)));
    }

    public long GetInt(int row, string column)
    {
        var (at, c) = Column(column);
        var r = _rows[row].AsSpan(at);
        return c.Type switch
        {
            DatColumnType.Bool => r[0],
            DatColumnType.I16 => BinaryPrimitives.ReadInt16LittleEndian(r),
            DatColumnType.U16 => BinaryPrimitives.ReadUInt16LittleEndian(r),
            DatColumnType.I32 => BinaryPrimitives.ReadInt32LittleEndian(r),
            DatColumnType.U32 or DatColumnType.EnumRow => BinaryPrimitives.ReadUInt32LittleEndian(r),
            DatColumnType.Row or DatColumnType.ForeignRow => (long)BinaryPrimitives.ReadUInt64LittleEndian(r),
            _ => throw new InvalidOperationException($"{_table.Name}.{column} is not a number"),
        };
    }

    /// <summary>The first row whose string column holds the value, or -1.</summary>
    public int Find(string column, string value)
    {
        for (int r = 0; r < _rows.Count; r++)
            if (GetString(r, column) == value) return r;
        return -1;
    }

    // ---- writing -------------------------------------------------------------------------------------------------

    /// <summary>Adds a copy of a row at the end and returns its index.</summary>
    public int AddCopyOf(int template)
    {
        _rows.Add((byte[])_rows[template].Clone());
        return _rows.Count - 1;
    }

    long AppendString(string value)
    {
        _variable.Position = _variable.Length;
        long at = _variable.Length;
        var bytes = Encoding.Unicode.GetBytes(value);
        _variable.Write(bytes);
        _variable.Write([0, 0, 0, 0]);
        return at;
    }

    public void SetString(int row, string column, string value)
    {
        var (at, c) = Column(column);
        if (c.Type != DatColumnType.String || c.IsArray) throw new InvalidOperationException($"{_table.Name}.{column} is not a string");
        BinaryPrimitives.WriteUInt64LittleEndian(_rows[row].AsSpan(at), (ulong)AppendString(value));
    }

    public void SetInt(int row, string column, long value)
    {
        var (at, c) = Column(column);
        var r = _rows[row].AsSpan(at);
        switch (c.Type)
        {
            case DatColumnType.Bool: r[0] = (byte)(value != 0 ? 1 : 0); break;
            case DatColumnType.I16: BinaryPrimitives.WriteInt16LittleEndian(r, (short)value); break;
            case DatColumnType.U16: BinaryPrimitives.WriteUInt16LittleEndian(r, (ushort)value); break;
            case DatColumnType.I32: BinaryPrimitives.WriteInt32LittleEndian(r, (int)value); break;
            case DatColumnType.U32 or DatColumnType.EnumRow: BinaryPrimitives.WriteUInt32LittleEndian(r, (uint)value); break;
            case DatColumnType.Row: BinaryPrimitives.WriteUInt64LittleEndian(r, value < 0 ? NullRow : (ulong)value); break;
            case DatColumnType.ForeignRow:
                BinaryPrimitives.WriteUInt64LittleEndian(r, value < 0 ? NullRow : (ulong)value);
                BinaryPrimitives.WriteUInt64LittleEndian(r[8..], value < 0 ? NullRow : 0);
                break;
            default: throw new InvalidOperationException($"{_table.Name}.{column} is not a number");
        }
    }

    public void SetFloat(int row, string column, float value)
    {
        var (at, c) = Column(column);
        if (c.Type != DatColumnType.F32 || c.IsArray) throw new InvalidOperationException($"{_table.Name}.{column} is not a float");
        BinaryPrimitives.WriteSingleLittleEndian(_rows[row].AsSpan(at), value);
    }

    /// <summary>Sets an array column of row keys or numbers (strings: <see cref="SetStringArray"/>).</summary>
    public void SetArray(int row, string column, IReadOnlyList<long> values)
    {
        var (at, c) = Column(column);
        if (!c.IsArray) throw new InvalidOperationException($"{_table.Name}.{column} is not an array");
        int size = Atom(c.Type);
        _variable.Position = _variable.Length;
        long start = _variable.Length;
        Span<byte> item = stackalloc byte[16];
        foreach (long v in values)
        {
            item.Clear();
            switch (c.Type)
            {
                case DatColumnType.Bool: item[0] = (byte)(v != 0 ? 1 : 0); break;
                case DatColumnType.I16 or DatColumnType.U16: BinaryPrimitives.WriteInt16LittleEndian(item, (short)v); break;
                case DatColumnType.I32 or DatColumnType.U32 or DatColumnType.EnumRow: BinaryPrimitives.WriteInt32LittleEndian(item, (int)v); break;
                case DatColumnType.Row: BinaryPrimitives.WriteUInt64LittleEndian(item, (ulong)v); break;
                case DatColumnType.ForeignRow: BinaryPrimitives.WriteUInt64LittleEndian(item, (ulong)v); break;
                default: throw new InvalidOperationException($"{_table.Name}.{column} holds {c.Type}");
            }
            _variable.Write(item[..size]);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(_rows[row].AsSpan(at), (ulong)values.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(_rows[row].AsSpan(at + 8), values.Count == 0 ? 0 : (ulong)start);
    }

    public void SetStringArray(int row, string column, IReadOnlyList<string> values)
    {
        var (at, c) = Column(column);
        if (!c.IsArray || c.Type != DatColumnType.String) throw new InvalidOperationException($"{_table.Name}.{column} is not a string array");
        var offsets = values.Select(AppendString).ToList();
        _variable.Position = _variable.Length;
        long start = _variable.Length;
        Span<byte> item = stackalloc byte[8];
        foreach (long o in offsets) { BinaryPrimitives.WriteUInt64LittleEndian(item, (ulong)o); _variable.Write(item); }
        BinaryPrimitives.WriteUInt64LittleEndian(_rows[row].AsSpan(at), (ulong)values.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(_rows[row].AsSpan(at + 8), values.Count == 0 ? 0 : (ulong)start);
    }

    /// <summary>Row keys of an array column (an array of rows or foreign rows).</summary>
    public IReadOnlyList<long> GetArray(int row, string column)
    {
        var (at, c) = Column(column);
        if (!c.IsArray) throw new InvalidOperationException($"{_table.Name}.{column} is not an array");
        long count = (long)BinaryPrimitives.ReadUInt64LittleEndian(_rows[row].AsSpan(at));
        long start = (long)BinaryPrimitives.ReadUInt64LittleEndian(_rows[row].AsSpan(at + 8));
        int size = Atom(c.Type);
        var v = _variable.GetBuffer().AsSpan(0, (int)_variable.Length);
        var result = new List<long>();
        for (long i = 0; i < count; i++)
        {
            var e = v[(int)(start + i * size)..];
            result.Add(size switch
            {
                1 => e[0],
                2 => BinaryPrimitives.ReadInt16LittleEndian(e),
                4 => BinaryPrimitives.ReadInt32LittleEndian(e),
                _ => (long)BinaryPrimitives.ReadUInt64LittleEndian(e),
            });
        }
        return result;
    }
}
