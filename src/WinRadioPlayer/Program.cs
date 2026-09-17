using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace WinRadioPlayer;

public static class Program
{
    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // One instance plays the radio. Starting the app again, for example from the jump list,
        // hands the command line to that instance instead of opening a second window.
        var mainInstance = AppInstance.FindOrRegisterForKey("WinRadioPlayer");
        if (!mainInstance.IsCurrent)
        {
            // Let the running instance bring its window to the front.
            AllowSetForegroundWindow(-1 /* ASFW_ANY */);
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
