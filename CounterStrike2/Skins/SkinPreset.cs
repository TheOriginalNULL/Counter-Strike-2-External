namespace CounterStrike2.Skins
{
    public class SkinPreset
    {
        public int   PaintKit    { get; set; } = 0;
        public float Wear        { get; set; } = 0.001f;
        public int   Seed        { get; set; } = 0;
        public int   StatTrak    { get; set; } = -1;  // -1 = disabled
        // true  → mesh mask 2  (old CS:GO UV layout: USP-S, M4A1-S, etc.)
        // false → mesh mask 1  (current CS2 UV layout: AK-47, AWP, etc.)
        public bool  LegacyModel { get; set; } = false;
    }
}
