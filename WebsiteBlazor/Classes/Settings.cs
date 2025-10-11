using AutoSpriteGenerator;

namespace WebsiteBlazor.Classes
{
    public class Settings
    {
        public int Dimension = 64;
        public int Margin = 0;
        
        public ColorStyle ColorStyle = ColorStyle.Harmonious;

        public int FrameCount = 8;
        public float AnimationIntesity = 1f;
        public int FrameDelayInMs = 80;

        public bool UseSeed = false;
        public int Seed = 27011998;
        public PaletteMode paletteMode = PaletteMode.Monochrome;
        public int numberOfColors = 4;

        public NoiseStyle NoiseStyle = NoiseStyle.Random;
        public LimbStyle limbStyle = LimbStyle.Random;
        public EyeStyle eyeStyle = EyeStyle.Random;
        public int? eyeCount = 7;
        public MouthStyle mouthStyle = MouthStyle.Random;
        public BodyArchetype bodyArchetype = BodyArchetype.Random;

        public bool UseSegments = false;
        public int NumberOfSegments = 2;
        public bool UseLobes = false;
        public int NumberOfLobes = 2;
        public bool UseHoles = false;
        public int NumberOfHoles = 1;
        public bool UseProtrusions = false;
        public bool UseMorphologicClean = true;
        public bool UseDilatation = false;
        public int NumberOfDilatations = 1;
        public bool UseErosion = false;
        public int NumberOfErosions = 1;
    }

    public enum NoiseStyle { Blobby, Balanced, Detailed, Random }

    public enum ColorStyle { Harmonious, RandomAccent, RandomDarken, RandomOutline }

    public enum PaletteMode
    {
        Monochrome,
        Analogous,
        Complementary,
        SplitComplementary,
        Triadic,
        TwoToneRandom,   // two strong hues, softer shading
        SoftStripes,      // produces an alternating-hue palette (for stripe-like bands)
        Random
    }

    public enum LimbStyle
    {
        Simple,     // thicker single stroke with slight jitter
        Thick,      // chunky tapering limb
        Segmented,  // series of overlapping rounded segments (like armor/sausages)
        Tentacle,   // wavy/tapering limb
        Jointed,    // two-segment limb with elbow/knee
        Paw,         // short stubby leg with rounded foot (good for legs)
        Random
    }

    public enum EyeStyle
    {
        BigSparkle = 0, // big round eyes, strong highlights + sparkles
        WideIris = 1, // iris nearly fills the sclera (cute anime-like)
        Almond = 2, // slightly horizontally stretched eye
        Sleepy = 3, // half-lidded, small iris
        Winking = 4, // closed eye (drawn as a cute curve)
        Button = 5, // tiny button-like eyes for extra cuteness
        Random = 6,
    }

    public enum MouthStyle
    {
        Smile = 0,   // small upward arc / "u" shape - cute
        SmallO = 1,  // tiny 'o' mouth
        Open = 2,    // open mouth (rect / small gap)
        Toothy = 3,  // small smile with a couple of teeth
        Grin = 4,    // wide grin (bigger smile + tiny teeth)
        Sad = 5,     // small downward arc
        Random = 6,
    }

    public enum BodyArchetype
    {
        Random = 0,    // choose a random archetype
        Normal,        // the default rounded body
        Thin,          // narrow tall body
        Tall,          // very tall column
        Wide,          // squat wide body
        Big,           // overall large / bulbous
        Tiny,          // very small compact body
        TopHeavy,      // big mass near the head
        BottomHeavy,   // heavy near the base
        Floating,      // body appears floating (gap near feet)
        Segmented,     // stacked segments (armor/sausage)
        MultiLobed,    // several side lobes
        Lopsided,      // asymmetric bulge to one side
        Hourglass,     // narrow waist, bulbs top+bottom
        Ringed,        // donut / hollow center
        Spiky,         // spikes/protrusions
        Tentacled,     // orb + tentacle-like lower bits
        Worm,          // long horizontal sausage
        Columnar,      // vertical column / pillar
        Bulbous,       // multiple stacked bulbs
        Armored,       // plates / overlapping ellipses
        Split,         // split into left/right halves
        Dripping,      // main body with droplet-like bits below
        Jelly,         // blob with wobbly margins (handled later by noise)
        Flat,          // very flat pancake
        Orbital,       // a central orb with smaller satellite blobs
        Asymmetric,    // deliberately uneven shape
        Layered,       // horizontal layers / plates
        Radial,        // central core with radial lobes
        TreeLike,      // trunk + branching masses
                       // add more as needed...
    }
}
