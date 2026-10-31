namespace ShowTime.Core.Ports;

/// <summary>
/// An image in the UI framework's own type (a WPF BitmapSource, an Avalonia Bitmap). The core never looks at its
/// pixels: it hands it to the ports that save, copy or edit it. One pixel is one physical screen pixel.
/// </summary>
public interface IImage : IDisposable
{
    int Width { get; }
    int Height { get; }
}
