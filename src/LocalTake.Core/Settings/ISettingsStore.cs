namespace LocalTake.Core.Settings;

public interface ISettingsStore
{
    /// <summary>The saved settings, or the defaults when the file is missing or unreadable.</summary>
    AppSettings Load();

    /// <summary>Writes the settings; throws on IO errors.</summary>
    void Save(AppSettings settings);

    /// <summary>
    /// For incidental state (last region, counter, editor preferences): an antivirus or a backup tool briefly locking
    /// the file must not interrupt a capture.
    /// </summary>
    bool TrySave(AppSettings settings);
}
