using System;
using System.Threading;
using System.Windows.Forms;

namespace Aj179PStat
{
    internal static class Program
    {
        private static Mutex? _singleInstanceMutex;

        [STAThread]
        static void Main()
        {
            const string mutexName = "Global\\Aj179PStat_SingleInstance_Mutex_v2";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Aj179PStat is already running in the background or system tray.", "Aj179PStat", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm());
            }
            finally
            {
                _singleInstanceMutex.ReleaseMutex();
            }
        }
    }
}