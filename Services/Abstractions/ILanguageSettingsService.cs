using Microsoft.Maui.Controls;

namespace Locksy.Services.Abstractions;

public interface ILanguageSettingsService
{
    bool IsPersian { get; }
    FlowDirection CurrentFlowDirection { get; }
    void SetLanguage(string languageCode);
}
