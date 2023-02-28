using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AddonScraper
{
    internal static class Program
    {
        
        [DllImport( "kernel32.dll" )]
        private static extern bool AttachConsole( int dwProcessId );
        
        [STAThread]
        private static void Main()
        {
            AttachConsole(-1); // makes Console.Write.. etc work
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}