using Locksy.Services.Abstractions;
using Microsoft.Maui.Controls;

namespace Locksy.Services;

public sealed class LanguageSettingsService : ILanguageSettingsService
{
    public bool IsPersian => LocalizationService.IsPersian;
    public FlowDirection CurrentFlowDirection => LocalizationService.CurrentFlowDirection;
    public void SetLanguage(string languageCode) => LocalizationService.SetLanguage(languageCode);
}
