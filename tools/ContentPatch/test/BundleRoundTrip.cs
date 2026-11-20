using GameServer.Data.Ggpk;
using LocalExile.ContentPatch;
var rnd = new Random(7);
foreach (int n in new[] { 1, 1000, 262144, 262145, 1_000_000 })
{
    var data = new byte[n]; rnd.NextBytes(data);
    var bundle = RawBundle.Write(data);
    var back = BundleFile.DecompressWhole(bundle, new ManagedOodleDecoder());
    var mine = RawBundle.TryReadRaw(bundle);
    Console.WriteLine($"{n,8}: OozSharp {(back.AsSpan().SequenceEqual(data) ? "identical" : "DIFFERENT")}, own reader {(mine is not null && mine.AsSpan().SequenceEqual(data) ? "identical" : "DIFFERENT")}, layout {BundleFile.Layout(bundle)}");
}
