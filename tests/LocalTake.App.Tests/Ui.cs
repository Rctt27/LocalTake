using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace LocalTake.App.Tests;

/// <summary>Avalonia without a screen, rendering with Skia so that exported images are real pixels.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class TestApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}

/// <summary>Runs test code on Avalonia's UI thread.</summary>
internal static class Ui
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    public static Task Run(Action action) => Session.Dispatch(action, TestContext.Current.CancellationToken);

    public static Task<T> Run<T>(Func<T> action) => Session.Dispatch(action, TestContext.Current.CancellationToken);

    public static Task Run(Func<Task> action) => Session.Dispatch(async () =>
    {
        await action();
        return true;
    }, TestContext.Current.CancellationToken);
}
