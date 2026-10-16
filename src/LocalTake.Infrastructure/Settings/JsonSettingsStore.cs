using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using LocalTake.Core.Settings;

namespace LocalTake.Infrastructure.Settings;

/// <summary>settings.json, as v0.1.0 wrote it: indented, enums by name, one key per property.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly IFileSystem _fileSystem;
    private readonly string _filePath;
    private readonly SettingsDefaults _defaults;
    private readonly JsonSerializerOptions _options;

    public JsonSettingsStore(IFileSystem fileSystem, string filePath, SettingsDefaults defaults)
    {
        _fileSystem = fileSystem;
        _filePath = filePath;
        _defaults = defaults;
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            // A key missing from the file keeps this platform's default rather than a C# default.
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    info =>
                    {
                        if (info.Type == typeof(AppSettings))
                            info.CreateObject = () => new AppSettings(_defaults);
                    }
                }
            }
        };
    }

    public AppSettings Load()
    {
        AppSettings? settings = null;
        try
        {
            if (_fileSystem.File.Exists(_filePath))
                settings = JsonSerializer.Deserialize<AppSettings>(_fileSystem.File.ReadAllText(_filePath), _options);
        }
#pragma warning disable CA1031 // A corrupt file must not prevent the app from starting; defaults take over and overwrite it on next save.
        catch (Exception)
#pragma warning restore CA1031
        {
            settings = null;
        }

        settings ??= new AppSettings(_defaults);
        if (string.IsNullOrWhiteSpace(settings.OutputFolder))
            settings.OutputFolder = _defaults.OutputFolder;
        return settings;
    }

    public void Save(AppSettings settings)
    {
        _fileSystem.Directory.CreateDirectory(_fileSystem.Path.GetDirectoryName(_filePath)!);
        _fileSystem.File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, _options));
    }

    public bool TrySave(AppSettings settings)
    {
        try
        {
            Save(settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
