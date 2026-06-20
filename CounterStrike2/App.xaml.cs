using System.Runtime.InteropServices;
using System.Windows;

namespace CounterStrike2
{
    public partial class App : Application
    {
        // Raise Windows multimedia timer resolution to 1ms so Task.Delay(8) actually
        // sleeps ~8ms instead of the default ~15.6ms.
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uPeriod);

        protected override void OnStartup(StartupEventArgs e)
        {
            timeBeginPeriod(1);
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            timeEndPeriod(1);
            base.OnExit(e);
        }
    }
}
