using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace AgentsPanelExtension;

public class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [MTAThread]
    public static void Main(string[] args)
    {
        Log.Info("Startup", "AgentsPanelExtension starting");

        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            Log.Info("ComServer", "RegisterProcessAsComServer mode detected");
            try
            {
                global::Shmuelie.WinRTServer.ComServer server = new();
                ManualResetEvent extensionDisposedEvent = new(false);
                AgentsPanelExtension extensionInstance = new(extensionDisposedEvent);
                server.RegisterClass<AgentsPanelExtension, IExtension>(() => extensionInstance);
                Log.Info("ComServer", "COM server registered, starting...");
                server.Start();
                Log.Info("ComServer", "COM server started, waiting for disposal signal");
                extensionDisposedEvent.WaitOne();
                Log.Info("ComServer", "Disposal signal received, stopping server");
                server.Stop();
                server.UnsafeDispose();
            }
            catch (Exception ex)
            {
                Log.Error("ComServer", "COM server failed", ex);
            }
        }
        else
        {
            _ = MessageBox(
                IntPtr.Zero,
                "Agents Panel for Command Palette is a background extension.\n\nTo use it, open PowerToys Command Palette and search for \"Agents Panel\".",
                "Agents Panel for Command Palette",
                0x40 /* MB_ICONINFORMATION */);
        }
    }
}
