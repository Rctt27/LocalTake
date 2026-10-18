using CoreFoundation;
using LocalTake.Core.Ports;

namespace LocalTake.Mac.Adapters;

/// <summary>AppKit's main thread, through the main dispatch queue, which the application's run loop drains.</summary>
internal sealed class MainThread : IUiThread
{
    public void Post(Action action) => DispatchQueue.MainQueue.DispatchAsync(action);
}
