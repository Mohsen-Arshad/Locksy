using Locksy.Services.Abstractions;
using MauiPreferences = Microsoft.Maui.Storage.Preferences;

namespace Locksy.Services.Preferences;

/// <summary>MAUI Preferences adapter. Keeps platform storage out of view models.</summary>
public sealed class MauiAppPreferencesService : IAppPreferencesService
{
    public bool GetBoolean(string key, bool defaultValue = false) => MauiPreferences.Default.Get(key, defaultValue);
    public int GetInt32(string key, int defaultValue = 0) => MauiPreferences.Default.Get(key, defaultValue);
    public long GetInt64(string key, long defaultValue = 0L) => MauiPreferences.Default.Get(key, defaultValue);
    public float GetSingle(string key, float defaultValue = 0f) => MauiPreferences.Default.Get(key, defaultValue);
    public void Set(string key, bool value) => MauiPreferences.Default.Set(key, value);
    public void Set(string key, int value) => MauiPreferences.Default.Set(key, value);
    public void Set(string key, long value) => MauiPreferences.Default.Set(key, value);
    public void Set(string key, float value) => MauiPreferences.Default.Set(key, value);
    public void Remove(string key) => MauiPreferences.Default.Remove(key);
}
