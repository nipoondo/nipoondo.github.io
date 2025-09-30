using AutoSpriteCreator;

namespace WebsiteBlazor.Classes
{
    public class Settings
    {
        public int Dimension = 64;
        public bool DrawPatterns = false;
        public int Margin = 0;
        public NoiseStyle NoiseStyle = NoiseStyle.Random;
        public int Generator = 1;
        public ColorStyle ColorStyle = ColorStyle.Harmonious;
        public int FrameCount = 8;
        public float AnimationIntesity = 1f;
        public int FrameDelayInMs = 80;
        public bool UseSeed = false;
        public int Seed = 27011998;
        public PaletteMode paletteMode = PaletteMode.Monochrome;
        public int numberOfColors = 4;
    }

    public enum NoiseStyle { Blobby, Balanced, Detailed, Random }

    public enum ColorStyle { Harmonious, RandomAccent, RandomDarken, RandomOutline }
}
