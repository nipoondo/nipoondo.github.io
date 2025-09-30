using AutoSpriteCreator;
using WebsiteBlazor.Classes;

namespace WebsiteBlazor.Pages
{
    public partial class Home
    {
        private string _sprite { get; set; } = string.Empty;
        private Settings _settings { get; set; } = new();

        protected override Task OnInitializedAsync()
        {
            //

            return base.OnInitializedAsync();
        }

        private void GenerateMonsterSprite()
        {
            if (_settings.UseSeed)
            {
                RNG.SetSeed(_settings.Seed);
            }
            else
            {
                RNG.Rand = new Random();
                _settings.Seed = RNG.Rand.Next();
                RNG.SetSeed(_settings.Seed);
            }

            _sprite = AdvancedPixelMonsterGenerator.MonsterMain(_settings);
        }

        private void NavigateToPalette()
        {
            nvgMgr.NavigateTo("/palette");
        }
    }
}
