using Locksy.Helpers;
using Locksy.Models;
using Locksy.Services.Abstractions;

namespace Locksy.Services;

/// <summary>Adapter around the existing localization catalog; keeps static implementation details outside ViewModels.</summary>
public sealed class LocalizationServiceAdapter : ILocalizationService
{
    public bool IsPersian => LocalizationService.IsPersian;
    public string Translate(string? value) => LocalizationService.Translate(value);
    public string PrepareTextForDisplay(string? value) => LocalizationService.PrepareTextForDisplay(value);
    public string PrepareTaskTextForDisplay(string? value, TaskTextDirection direction) =>
        TaskTextDirectionHelper.PrepareForDisplay(value, direction, IsPersian);
    public string LocalizeDigits(string value) => LocalizationService.LocalizeDigits(value);
}
