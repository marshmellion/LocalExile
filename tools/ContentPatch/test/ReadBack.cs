using GameServer.Data.Dat; using GameServer.Data.Ggpk;
using var fs = GameFileSystem.Open(args[0]);
var schema = DatSchema.LoadFile("data/schema/schema.min.json")["WorldAreas"];
var rows = DatFile.Parse(fs.ReadFile("Data/WorldAreas.datc64")).ReadRows(schema);
var town = rows.First(r => (string?)r["Id"].Raw == "1_1_town");
var mine = rows.Where(r => (string?)r["Id"].Raw == "LocalExile_Test").ToList();
var conns = mine.Count > 0 ? mine[0]["Connections_WorldAreasKeys"].Raw : null;
Console.WriteLine($"server reader: {rows.Count} areas; town name '{town["Name"].Raw}'; new rows {mine.Count}" + (mine.Count > 0 ? $": '{mine[0]["Name"].Raw}' level {mine[0]["AreaLevel"].Raw}, connections {System.Text.Json.JsonSerializer.Serialize(conns)}" : ""));
Console.WriteLine($"art file: {(fs.TryReadFile("Art/LocalExile/hello.txt", out var b) ? System.Text.Encoding.UTF8.GetString(b).Trim() : "missing")}");
