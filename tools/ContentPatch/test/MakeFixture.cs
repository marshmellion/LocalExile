// Builds the fixture's index and data bundle: one bundle "Data/Tables" holding Data/WorldAreas.datc64.
using LocalExile.ContentPatch;
using System.Buffers.Binary; using System.Text;
var table = File.ReadAllBytes("/home/claude/gamefiles/data/worldareas.datc64");
File.WriteAllBytes("fixture/Tables.bundle.bin", RawBundle.Write(table));
using var ms = new MemoryStream();
void I32(int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); ms.Write(b); }
I32(1); var name = Encoding.UTF8.GetBytes("Tables"); I32(name.Length); ms.Write(name); I32(table.Length);
I32(1); var rec = new byte[20]; BinaryPrimitives.WriteUInt64LittleEndian(rec, IndexEditor.PathHash("Data/WorldAreas.datc64")); ms.Write(rec); // bundle 0, offset 0
BinaryPrimitives.WriteInt32LittleEndian(rec.AsSpan(16), table.Length); ms.Position -= 20; ms.Write(rec);
I32(0);                                   // no directories
ms.Write(RawBundle.Write([]));            // an empty path table
File.WriteAllBytes("fixture/_.index.bin", RawBundle.Write(ms.ToArray()));
Console.WriteLine("fixture index and bundle written");
