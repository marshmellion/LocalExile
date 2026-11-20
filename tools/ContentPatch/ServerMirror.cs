using Microsoft.Data.Sqlite;

namespace LocalExile.ContentPatch;

/// <summary>
/// The servers read the client's tables from <c>data/client.db</c> (built from sql/, one SQLite table per client table:
/// <c>_row</c>, then the schema's columns; arrays as JSON, row references as row numbers), never from the client. So
/// every row the patcher adds or changes in the client is also written there, and both sides agree on a new item's
/// rows and ids.
/// <para>What an apply wrote is undone by the next one (and by restore) before it writes again: the rows it added go
/// (the tables' original row counts are kept in <c>localexile_content_tables</c>) and the cells it changed get their
/// old values back (<c>localexile_content_undo</c>).</para>
/// </summary>
public sealed class ServerMirror(string database)
{
    readonly List<Action<SqliteConnection, SqliteTransaction>> _changes = [];

    public sealed record Value(object? Sql);

    public void Add(string table, int row, int template) =>
        _changes.Add((db, tx) =>
        {
            KeepOriginalCount(db, tx, table);
            var columns = Columns(db, tx, table).Where(c => c != "_row").Select(c => $"\"{c}\"").ToList();
            Run(db, tx, $"DELETE FROM \"{table}\" WHERE _row = $r", ("$r", row));
            Run(db, tx, $"INSERT INTO \"{table}\" (_row, {string.Join(", ", columns)}) SELECT $r, {string.Join(", ", columns)} FROM \"{table}\" WHERE _row = $t",
                ("$r", row), ("$t", template));
        });

    public void Set(string table, int row, string column, object? value) =>
        _changes.Add((db, tx) =>
        {
            if (!Columns(db, tx, table).Contains(column)) return;   // a column client.db does not keep
            KeepOriginalCount(db, tx, table);
            // An original row's old value, once, so the next apply and restore can put it back.
            if (row < OriginalCount(db, tx, table))
                Run(db, tx, $"INSERT OR IGNORE INTO localexile_content_undo (tbl, row, col, value) SELECT $tb, $r, $c, \"{column}\" FROM \"{table}\" WHERE _row = $r",
                    ("$tb", table), ("$r", row), ("$c", column));
            Run(db, tx, $"UPDATE \"{table}\" SET \"{column}\" = $v WHERE _row = $r", ("$v", value ?? DBNull.Value), ("$r", row));
        });

    /// <summary>A row only the servers have (a client.db table that is not a client table, such as item_types): a copy of
    /// the first row whose columns equal <paramref name="copyOf"/>, added at the end, with <paramref name="set"/> written
    /// over it. Taken back like the patcher's other rows.</summary>
    public void AddServerRow(string table, IReadOnlyDictionary<string, object?> copyOf, IReadOnlyDictionary<string, object?> set, string label) =>
        _changes.Add((db, tx) =>
        {
            var columns = Columns(db, tx, table);
            foreach (string c in copyOf.Keys.Concat(set.Keys))
                if (!columns.Contains(c)) throw new InvalidDataException($"{label}: {database} table {table} has no column {c}");
            KeepOriginalCount(db, tx, table);
            using var find = Command(db, tx, $"SELECT _row FROM \"{table}\" WHERE " +
                string.Join(" AND ", copyOf.Keys.Select((c, i) => $"\"{c}\" = $w{i}")) + " ORDER BY _row LIMIT 1");
            int n = 0;
            foreach (var v in copyOf.Values) find.Parameters.AddWithValue($"$w{n++}", v ?? DBNull.Value);
            if (find.ExecuteScalar() is not long template) throw new InvalidDataException($"{label}: no {table} row to copy");
            using var next = Command(db, tx, $"SELECT COALESCE(MAX(_row) + 1, 0) FROM \"{table}\"");
            long row = (long)next.ExecuteScalar()!;
            var rest = columns.Where(c => c != "_row").Select(c => $"\"{c}\"").ToList();
            Run(db, tx, $"INSERT INTO \"{table}\" (_row, {string.Join(", ", rest)}) SELECT $r, {string.Join(", ", rest)} FROM \"{table}\" WHERE _row = $t",
                ("$r", row), ("$t", template));
            foreach (var (column, value) in set)
                Run(db, tx, $"UPDATE \"{table}\" SET \"{column}\" = $v WHERE _row = $r", ("$v", value ?? DBNull.Value), ("$r", row));
        });

    /// <summary>Undoes the previous apply, then writes this one's changes, in one transaction.</summary>
    public void Commit(Action<string> log)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        Undo(db, tx);
        foreach (var change in _changes) change(db, tx);
        tx.Commit();
        log($"wrote {_changes.Count} change(s) into {database}");
    }

    /// <summary>Takes back everything the patcher wrote into the database.</summary>
    public void Restore(Action<string> log)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        Undo(db, tx);
        tx.Commit();
        log($"{database} is back to its tables as built from sql/");
    }

    SqliteConnection Open()
    {
        if (!File.Exists(database)) throw new FileNotFoundException($"{database} is missing: start the servers once so they build it", database);
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        db.Open();
        Run(db, null, "PRAGMA busy_timeout=10000");
        Run(db, null, "CREATE TABLE IF NOT EXISTS localexile_content_tables (tbl TEXT PRIMARY KEY, original_count INTEGER NOT NULL)");
        Run(db, null, "CREATE TABLE IF NOT EXISTS localexile_content_undo (tbl TEXT, row INTEGER, col TEXT, value, PRIMARY KEY (tbl, row, col))");
        return db;
    }

    static void Undo(SqliteConnection db, SqliteTransaction tx)
    {
        var undo = new List<(string Table, long Row, string Column, object? Value)>();
        using (var c = Command(db, tx, "SELECT tbl, row, col, value FROM localexile_content_undo"))
        using (var r = c.ExecuteReader())
            while (r.Read()) undo.Add((r.GetString(0), r.GetInt64(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetValue(3)));
        foreach (var (table, row, column, value) in undo)
            Run(db, tx, $"UPDATE \"{table}\" SET \"{column}\" = $v WHERE _row = $r", ("$v", value ?? DBNull.Value), ("$r", row));
        var counts = new List<(string Table, long Count)>();
        using (var c = Command(db, tx, "SELECT tbl, original_count FROM localexile_content_tables"))
        using (var r = c.ExecuteReader())
            while (r.Read()) counts.Add((r.GetString(0), r.GetInt64(1)));
        foreach (var (table, count) in counts) Run(db, tx, $"DELETE FROM \"{table}\" WHERE _row >= $n", ("$n", count));
        Run(db, tx, "DELETE FROM localexile_content_undo");
        Run(db, tx, "DELETE FROM localexile_content_tables");
    }

    static void KeepOriginalCount(SqliteConnection db, SqliteTransaction tx, string table) =>
        Run(db, tx, $"INSERT OR IGNORE INTO localexile_content_tables (tbl, original_count) SELECT $t, COALESCE(MAX(_row) + 1, 0) FROM \"{table}\"", ("$t", table));

    static long OriginalCount(SqliteConnection db, SqliteTransaction tx, string table)
    {
        using var c = Command(db, tx, "SELECT original_count FROM localexile_content_tables WHERE tbl = $t");
        c.Parameters.AddWithValue("$t", table);
        return c.ExecuteScalar() is long n ? n : long.MaxValue;
    }

    readonly Dictionary<string, HashSet<string>> _columns = new(StringComparer.Ordinal);

    HashSet<string> Columns(SqliteConnection db, SqliteTransaction tx, string table)
    {
        if (_columns.TryGetValue(table, out var known)) return known;
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var c = Command(db, tx, $"PRAGMA table_info(\"{table}\")");
        using var r = c.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(1));
        if (set.Count == 0) throw new InvalidDataException($"{database} has no table {table}");
        return _columns[table] = set;
    }

    static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql)
    {
        var c = db.CreateCommand();
        c.CommandText = sql;
        c.Transaction = tx;
        return c;
    }

    static void Run(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object Value)[] parameters)
    {
        using var c = Command(db, tx, sql);
        foreach (var (name, value) in parameters) c.Parameters.AddWithValue(name, value);
        c.ExecuteNonQuery();
    }
}
