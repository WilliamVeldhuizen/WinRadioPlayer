using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using ZapperRadio.Core.Shell;

namespace ZapperRadio;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        // Started from the jump list while the app was closed.
        if (JumpListCommand.Parse(Environment.GetCommandLineArgs()) is { } command)
        {
            _window.ViewModel.Execute(command);
        }

        AppInstance.GetCurrent().Activated += OnActivated;
    }

    /// <summary>Another start of the app was redirected here (see Program).</summary>
    private void OnActivated(object? sender, AppActivationArguments args)
    {
        var commandLine = (args.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs)?.Arguments ?? "";
        var command = JumpListCommand.Parse(SplitCommandLine(commandLine));
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            if (command is null)
            {
                // Started normally, e.g. from the Start menu: show the window that is already open.
                _window.BringToFront();
            }
            else
            {
                _window.ViewModel.Execute(command);
            }
        });
    }

    private static string[] SplitCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return [];
        }

        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == 0)
        {
            return [];
        }

        try
        {
            return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!).ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
