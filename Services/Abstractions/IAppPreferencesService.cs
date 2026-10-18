namespace Locksy.Services.Abstractions;

/// <summary>Typed, app-scoped access to device preferences.</summary>
public interface IAppPreferencesService
{
    bool GetBoolean(string key, bool defaultValue = false);
    int GetInt32(string key, int defaultValue = 0);
    long GetInt64(string key, long defaultValue = 0L);
    float GetSingle(string key, float defaultValue = 0f);
    void Set(string key, bool value);
    void Set(string key, int value);
    void Set(string key, long value);
    void Set(string key, float value);
    void Remove(string key);
}
