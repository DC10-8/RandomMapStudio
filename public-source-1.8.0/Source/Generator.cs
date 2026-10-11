using System;
using System.Globalization;


namespace RandomMapStudio
{
    public sealed class Settings
    {
        public double Land, Sea, Mountains;
        public bool River;
        public static Settings Parse(string land, string sea, string mountains, bool river)
        {
            var s = new Settings { Land = Number(land), Sea = Number(sea), Mountains = Number(mountains), River = river };
            if (Math.Abs(s.Land + s.Sea + s.Mountains - 100) > .001)
                throw new ArgumentException(L.T("陸地・海・山地の合計を100％にしてください。", "Land, sea and mountains must total 100%."));
            return s;
        }
        static double Number(string value)
        {
            double n;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out n) ||
                double.IsNaN(n) || double.IsInfinity(n) || n < 0 || n > 100)
                throw new ArgumentException(L.T("割合は0〜100の数値で入力してください（小数点は .）。", "Enter percentages from 0 to 100 (use . for decimals)."));
            return n;
        }
    }

    public sealed class Map
    {
        public readonly int Size;
        public readonly ushort[] Heights;
        public const double CellSize = 16;
        // CS1 and HundredTiles keep the original minimum world coordinate.
        public const double WorldMin = -8640;
        public Map(int size)
        {
            if (size < 2 || (long)size * size > int.MaxValue) throw new ArgumentOutOfRangeException("size");
            Size = size; Heights = new ushort[checked(size * size)];
        }
        public static int DetectSize(int length)
        {
            int size = (int)Math.Sqrt(length);
            if (size < 2 || (long)size * size != length)
                throw new ArgumentException(L.T("地形データが正方形の高さグリッドではありません（要素数: ", "Terrain data is not a square height grid (samples: ") + length + "）。");
            return size;
        }
        public double WorldCoordinate(int sample) { return WorldMin + sample * CellSize; }
        public double WidthKm { get { return (Size - 1) * CellSize / 1000; } }
        public bool IsWaterAt(double worldX,double worldZ)
        {
            double height;
            return TryGroundHeight(worldX,worldZ,out height) && height<40;
        }
        public bool TryGroundHeight(double worldX,double worldZ,out double height)
        {
            height=0;
            double x=(worldX-WorldMin)/CellSize,z=(worldZ-WorldMin)/CellSize;
            if(double.IsNaN(x)||double.IsNaN(z)||x<0||z<0||x>Size-1||z>Size-1)return false;
            int x0=(int)Math.Floor(x),z0=(int)Math.Floor(z);
            int x1=Math.Min(x0+1,Size-1),z1=Math.Min(z0+1,Size-1);
            double u=x-x0,v=z-z0;
            double a=Heights[z0*Size+x0]*(1-u)+Heights[z0*Size+x1]*u;
            double b=Heights[z1*Size+x0]*(1-u)+Heights[z1*Size+x1]*u;
            height=(a*(1-v)+b*v)/64;
            return true;
        }
        public static double ExpandedTreeAxis(short code,ushort flags,ushort marker)
        { return (code+((flags&marker)!=0?65536:0))*0.263671875; }
        public System.Collections.Generic.IEnumerable<int> RiverSourceIndices()
        {
            if (!River) yield break;
            for (int z = RiverStart; z <= RiverEnd; z += 64)
                yield return z * Size + (int)Math.Round(RiverX(z / (double)(Size - 1), RiverPhase) * (Size - 1));
            if ((RiverEnd - RiverStart) % 64 != 0)
                yield return RiverEnd * Size + (int)Math.Round(RiverX(RiverEnd / (double)(Size - 1), RiverPhase) * (Size - 1));
        }
        public int Seed, SeaCount, MountainCount, RiverCount;
        public bool River;
        public int RiverStart = -1, RiverEnd = -1;
        public double RiverPhase;
        public static double RiverX(double z, double phase)
        { return .5 + .16 * Math.Sin(z * 7 + phase) + .06 * Math.Sin(z * 17 + phase); }
    }

    public static class Generator
    {
        // Pure managed code: safe to run on a worker, independent of Unity.
        public static Map Generate(Settings settings, int seed, Func<bool> cancelled, int size = 1081)
        {
            var map = new Map(size) { Seed = seed, River = settings.River, RiverPhase = new Random(seed).NextDouble() * Math.PI * 2 };
            int count = map.Heights.Length;
            var field = new double[count];
            var order = new int[count];
            for (int z = 0; z < map.Size; z++)
            {
                if (cancelled()) throw new OperationCanceledException();
                for (int x = 0; x < map.Size; x++)
                {
                    int i = z * map.Size + x;
                    double u = x / (double)(size - 1), v = z / (double)(size - 1);
                    field[i] = Noise(u * 4, v * 4, seed) + .38 * Noise(u * 9, v * 9, seed + 1) +
                        .14 * Noise(u * 22, v * 22, seed + 2) + .05 * Noise(u * 55, v * 55, seed + 3);
                    order[i] = i;
                }
            }
            Array.Sort(order, delegate(int a, int b) { int c = field[a].CompareTo(field[b]); return c == 0 ? a.CompareTo(b) : c; });
            if (cancelled()) throw new OperationCanceledException();
            int sea = (int)Math.Round(count * settings.Sea / 100), mountains = (int)Math.Round(count * settings.Mountains / 100);
            int mountainStart = count - mountains;
            double seaCut = sea == 0 ? field[order[0]] : field[order[Math.Min(sea, count - 1)]];
            double mountainCut = mountains == 0 ? field[order[count - 1]] : field[order[Math.Min(mountainStart, count - 1)]];
            double min = field[order[0]], max = field[order[count - 1]];
            for (int rank = 0; rank < count; rank++)
            {
                int i = order[rank]; double n = field[i], height;
                if (rank < sea) { height = 8 + 31 * Fraction(n, min, seaCut); map.SeaCount++; }
                else if (rank >= mountainStart) { height = 120 + 680 * Math.Pow(Fraction(n, mountainCut, max), 1.35); map.MountainCount++; }
                else height = 41 + 78 * Fraction(n, seaCut, mountainCut);
                map.Heights[i] = (ushort)Math.Round(height * 64);
            }
            if (settings.River)
            {
                // Choose one continuous land reach. Stop at its first receiving
                // waterbody; never resume carving on the far side of a lake.
                int bestStart = -1, bestEnd = -1, start = -1;
                for (int z = 0; z <= size; z++)
                {
                    int x = z == size ? 0 : (int)Math.Round(Map.RiverX(z / (double)(size - 1), map.RiverPhase) * (size - 1));
                    bool dry = z < size && map.Heights[z * size + x] >= 40 * 64;
                    if (dry && start < 0) start = z;
                    if (!dry && start >= 0)
                    {
                        int end = z - 1;
                        if (end - start > bestEnd - bestStart) { bestStart = start; bestEnd = end; }
                        start = -1;
                    }
                }
                map.River = bestStart >= 0;
                if (map.River)
                {
                    // Keep the entire dry reach: each end touches either the
                    // map boundary or the first original water cell next to it.
                    map.RiverStart = bestStart; map.RiverEnd = bestEnd;
                }
                for (int z = map.RiverStart; map.River && z <= map.RiverEnd; z++)
                {
                    if (cancelled()) throw new OperationCanceledException();
                    double center = Map.RiverX(z / (double)(size - 1), map.RiverPhase) * (size - 1);
                    for (int x = Math.Max(0, (int)center - 24); x <= Math.Min(size - 1, (int)center + 24); x++)
                    {
                        int i = z * map.Size + x;
                        if (map.Heights[i] < 40 * 64) continue; // Preserve every original sea/lake sample.
                        double d = Math.Abs(x - center);
                        // 20 m depth at the center, rounded bed and smooth banks.
                        double blend = Math.Max(0, Math.Min(1, (d - 8) / 16));
                        blend = blend * blend * (3 - 2 * blend);
                        double bed = d <= 8 ? 20 + 18 * (d / 8) * (d / 8) :
                            38 + (map.Heights[i] / 64.0 - 38) * blend;
                        ushort h = (ushort)Math.Round(bed * 64);
                        if (h < map.Heights[i]) map.Heights[i] = h;
                        if (d <= 8) map.RiverCount++;
                    }
                }
            }
            return map;
        }
        static double Fraction(double n, double a, double b) { return b - a < 1e-12 ? .5 : Math.Max(0, Math.Min(1, (n - a) / (b - a))); }
        static double Noise(double x, double z, int seed)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            double u = x - ix, v = z - iz; u = u * u * (3 - 2 * u); v = v * v * (3 - 2 * v);
            double a = Hash(ix, iz, seed), b = Hash(ix + 1, iz, seed), c = Hash(ix, iz + 1, seed), d = Hash(ix + 1, iz + 1, seed);
            return (a + (b - a) * u) * (1 - v) + (c + (d - c) * u) * v;
        }
        static double Hash(int x, int z, int seed)
        {
            unchecked { uint h = (uint)(x * 374761393 + z * 668265263 + seed * 144269); h = (h ^ (h >> 13)) * 1274126177; return (h ^ (h >> 16)) / (double)uint.MaxValue; }
        }
    }
}
