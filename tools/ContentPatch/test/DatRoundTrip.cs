using GameServer.Data.Dat;
using LocalExile.ContentPatch;
var schema = DatSchema.LoadFile("/home/claude/localexile/data/schema/schema.min.json");
int ok = 0;
foreach (var f in Directory.GetFiles("/home/claude/gamefiles/data", "*.datc64"))
{
    string table = Path.GetFileNameWithoutExtension(f);
    var bytes = File.ReadAllBytes(f);
    if (!schema.TryGetValue(table, out var t)) { Console.WriteLine($"{table}: no schema"); continue; }
    try
    {
        var e = DatEditor.Parse(bytes, t);
        bool same = e.Write().AsSpan().SequenceEqual(bytes);
        Console.WriteLine($"{table}: rows {e.RowCount} x {e.RowLength}, unchanged round trip {(same ? "identical" : "DIFFERENT")}");
        if (same) ok++;
    }
    catch (Exception ex) { Console.WriteLine($"{table}: {ex.Message}"); }
}
// A new row: a copy of Lioneye's Watch with another id, name and an array changed; read back with the repo's reader.
var wa = DatEditor.Parse(File.ReadAllBytes("/home/claude/gamefiles/data/worldareas.datc64"), schema["WorldAreas"]);
int town = wa.Find("Id", "1_1_town");
int row = wa.AddCopyOf(town);
wa.SetString(row, "Id", "LocalExile_TestArea");
wa.SetString(row, "Name", "The Test Grounds");
wa.SetInt(row, "AreaLevel", 77);
var rows = DatFile.Parse(wa.Write()).ReadRows(schema["WorldAreas"]);
var a = rows[row]; var b = rows[town];
Console.WriteLine($"new row {row}: Id={a["Id"].Raw} Name={a["Name"].Raw} AreaLevel={a["AreaLevel"].Raw}; template still Id={b["Id"].Raw} Name={b["Name"].Raw}");
Console.WriteLine($"identical round trips: {ok}");
