namespace Locksy.Services.Abstractions;

/// <summary>Provides localized strings and display-only text transformations to presentation models.</summary>
public interface ILocalizationService
{
    bool IsPersian { get; }
    string Translate(string? value);
    string PrepareTextForDisplay(string? value);
    string LocalizeDigits(string value);
}
