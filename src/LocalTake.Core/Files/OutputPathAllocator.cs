using System.IO.Abstractions;

namespace LocalTake.Core.Files;

/// <summary>Free paths in the output folder, named after the pattern.</summary>
public sealed class OutputPathAllocator(IFileSystem fileSystem, TimeProvider clock)
{
    /// <summary>
    /// A path not taken yet: "name (2).png" when "name.png" exists. Does not consume %NUM%: call
    /// <see cref="FileCounter.Consume"/> once the file is written.
    /// </summary>
    public string Next(string folder, string pattern, int counter, string extension)
    {
        fileSystem.Directory.CreateDirectory(folder);
        var baseName = FileNamePattern.Expand(pattern, counter, clock.GetLocalNow().DateTime);
        var path = fileSystem.Path.Combine(folder, baseName + extension);
        for (var i = 2; fileSystem.File.Exists(path); i++)
            path = fileSystem.Path.Combine(folder, $"{baseName} ({i}){extension}");
        return path;
    }

    /// <summary>The name a Save As dialog suggests, without extension.</summary>
    public string SuggestedName(string pattern, int counter) =>
        FileNamePattern.Expand(pattern, counter, clock.GetLocalNow().DateTime);
}
