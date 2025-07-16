using System.Globalization;
using FanX.Resources;

namespace FanX.Services;

public class LocalizationService
{
    public event Action? OnLanguageChanged;
    
    private CultureInfo _currentCulture = CultureInfo.CurrentCulture;
    
    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture != value)
            {
                _currentCulture = value;
                CultureInfo.CurrentCulture = value;
                CultureInfo.CurrentUICulture = value;
                Localization.Culture = value;
                OnLanguageChanged?.Invoke();
            }
        }
    }
    
    public List<CultureInfo> SupportedCultures { get; } = new()
    {
        new CultureInfo("en"),
        new CultureInfo("zh")
    };
    
    public string GetLocalizedString(string key)
    {
        return Localization.ResourceManager.GetString(key, _currentCulture) ?? key;
    }
    
    public string GetLocalizedString(string key, params object[] args)
    {
        var format = Localization.ResourceManager.GetString(key, _currentCulture) ?? key;
        return string.Format(format, args);
    }
    
    public void SetLanguage(string cultureName)
    {
        var culture = SupportedCultures.FirstOrDefault(c => c.Name == cultureName);
        if (culture != null)
        {
            CurrentCulture = culture;
        }
    }
    
    public void SetLanguage(CultureInfo culture)
    {
        if (SupportedCultures.Any(c => c.Name == culture.Name))
        {
            CurrentCulture = culture;
        }
    }
} 