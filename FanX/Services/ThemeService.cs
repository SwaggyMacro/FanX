using MudBlazor;

namespace FanX.Services
{
    public class ThemeService
    {
        private readonly BrowserStorageService _browserStorage;
        public bool IsDarkMode { get; private set; }
        public MudTheme CurrentTheme { get; private set; }

        public event Action? OnThemeChanged;

        public ThemeService(BrowserStorageService browserStorage)
        {
            _browserStorage = browserStorage;
            IsDarkMode = false; // 默认浅色模式
            CurrentTheme = new MudTheme();
        }

        public async Task InitializeAsync()
        {
            var isDarkModeString = await _browserStorage.GetItemAsync("isDarkMode");
            IsDarkMode = isDarkModeString == "true";
            SetTheme();
        }

        public void ToggleTheme()
        {
            IsDarkMode = !IsDarkMode;
            SetTheme();
            _ = _browserStorage.SetItemAsync("isDarkMode", IsDarkMode.ToString().ToLower());
        }

        public async Task ToggleThemeAsync()
        {
            IsDarkMode = !IsDarkMode;
            await _browserStorage.SetItemAsync("isDarkMode", IsDarkMode.ToString().ToLower());
            SetTheme();
        }

        private void SetTheme()
        {
            CurrentTheme = IsDarkMode ? GenerateDarkTheme() : new MudTheme();
            OnThemeChanged?.Invoke();
        }

        private MudTheme GenerateDarkTheme()
        {
            return new MudTheme
            {
                PaletteDark = new PaletteDark
                {
                    Primary = Colors.Blue.Lighten1,
                    Secondary = Colors.Green.Accent4,
                    AppbarBackground = "#27272f",
                    DrawerBackground = "#27272f",
                },
                LayoutProperties = new LayoutProperties
                {
                    DrawerWidthLeft = "260px",
                    DrawerWidthRight = "300px"
                }
            };
        }
    }
} 