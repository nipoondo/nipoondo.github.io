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
    }

    public enum NoiseStyle { Blobby, Balanced, Detailed, Random }

    public enum ColorStyle { Harmonious, RandomAccent, RandomDarken, RandomOutline }
}
