using LocalTake.Core.Settings;

namespace LocalTake.Core.Files;

/// <summary>The %NUM% counter: it only advances once a file is actually written.</summary>
public sealed class FileCounter(AppSettings settings, ISettingsStore store)
{
    public void Consume()
    {
        if (!FileNamePattern.UsesCounter(settings.FileNamePattern))
            return;
        settings.Counter++;
        store.TrySave(settings);
    }

    /// <summary>
    /// Takes %NUM% when a recording starts, so that an image saved meanwhile gets the next number instead of the same
    /// one. Returns the number taken, or null when the pattern has no counter.
    /// </summary>
    public int? Reserve()
    {
        var before = settings.Counter;
        Consume();
        return settings.Counter != before ? before : null;
    }

    /// <summary>Gives a reserved number back, unless another file has taken a later one since.</summary>
    public void Release(int? reserved)
    {
        if (reserved is not { } number || settings.Counter != number + 1)
            return;
        settings.Counter = number;
        store.TrySave(settings);
    }
}
