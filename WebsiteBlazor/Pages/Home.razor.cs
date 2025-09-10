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
            _sprite = AdvancedPixelMonsterGenerator.Main(_settings);
        }
    }
}
