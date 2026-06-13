using System.Runtime.InteropServices;

namespace Daeanne.Tray;

static class Program
{
    // Set a stable AUMID so Windows notification header shows "Daeanne"
    // instead of the auto-generated "NotifyIconGeneratedAumid_XXXX".
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    [STAThread]
    static void Main()
    {
        SetCurrentProcessExplicitAppUserModelID("Daeanne.Tray");
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}