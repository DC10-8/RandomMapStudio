using System;

namespace RandomMapStudio
{
    public static class WaterFill
    {
        public static ushort[] Depths(ushort[] ground, ushort level)
        {
            var depths = new ushort[ground.Length];
            for (int i = 0; i < ground.Length; i++) depths[i] = ground[i] < level ? (ushort)(level - ground[i]) : (ushort)0;
            return depths;
        }
        public static ulong[] Masks(ushort[] depths, int size)
        {
            if ((long)size * size != depths.Length || size < 2) throw new ArgumentException("Invalid water grid");
            int cells = (size + 63) / 64;
            var masks = new ulong[64];
            for (int z = 0; z < size; z++) for (int x = 0; x < size; x++)
                if (depths[z * size + x] != 0) masks[z / cells] |= 1UL << (x / cells);
            return masks;
        }
    }
}
