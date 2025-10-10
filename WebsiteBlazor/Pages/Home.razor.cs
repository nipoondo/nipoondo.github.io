using System.Runtime;
using AutoSpriteGenerator;
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
            if (!_settings.UseSeed)
            {
                _settings.Seed = Environment.TickCount;
            }

            DeterministicRandom.Initialize(_settings.Seed);


            _sprite = AdvancedPixelMonsterGenerator.MonsterMain(_settings);
        }

        private void NavigateToPalette()
        {
            nvgMgr.NavigateTo("/palette");
        }
    }
}
