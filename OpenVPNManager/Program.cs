using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Resources;
using System.Threading;
using System.Windows.Forms;
using OpenVPNUtils;

[module: SuppressMessage("Microsoft.Naming",
    "CA1709:IdentifiersShouldBeCasedCorrectly",
    Scope = "namespace", Target = "OpenVPNManager",
    MessageId = "VPN")]
namespace OpenVPNManager
{
    /// <summary>
    /// Main program
    /// </summary>

    static class Program
    {
        /// <summary>
        /// Holds a reference to the ResourceManager which contains
        /// localized strings.
        /// </summary>
        public static ResourceManager res = new ResourceManager(
            "OpenVPNManager.lang.strings",
            System.Reflection.Assembly.GetExecutingAssembly());

        private static FrmGlobalStatus m_mainform;

        static bool CommandLineArgumentsContain(List<string> arguments, String parameter)
        {
            foreach (String arg in arguments)
            {
                if ("/\\-".Contains(arg.Substring(0, 1)))
                {
                    String param = arg.Substring(1);
                    if (parameter.Equals(param, StringComparison.InvariantCultureIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Main entry point
        /// </summary>
        /// <param name="args">Parameters passed to the binary</param>
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                List<string> arguments = new List<string>(args);

                Microsoft.Win32.SystemEvents.PowerModeChanged +=
                    new Microsoft.Win32.PowerModeChangedEventHandler(
                        SystemEvents_PowerModeChanged);

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                Mutex appSingleton = new Mutex(false, Application.ProductName + ".SingleInstance");
                m_mainform = new FrmGlobalStatus();
                Application.Run(m_mainform);

                appSingleton.Close();
            }
            catch (Exception ex)
            {
                //In case of 'something terible' dont disappear without a message.
                MessageBox.Show(ex.ToString());
            }
        }

        /// <summary>
        /// Called when the PC is hibernated or woke up.
        /// Used to save and restore all vpn networks.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        static void SystemEvents_PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
                m_mainform.CloseAll();

           }
    }
}
