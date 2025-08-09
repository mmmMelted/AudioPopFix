using System;
using System.Windows.Forms;

namespace AudioPopFixTray
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayAppContext());
        }
    }
}
