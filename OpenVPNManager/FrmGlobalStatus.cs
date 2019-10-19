using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using OpenVPNUtils.States;
using System.Configuration;
using System.Threading;
using OpenVPNUtils;

namespace OpenVPNManager
{
    /// <summary>
    /// Holds all VPN configurations, represents the status.
    /// </summary>
    public partial class FrmGlobalStatus : Form
    {

        #region variables

        /// <summary>
        /// Represents a list of available vpn configuration.
        /// </summary>
        private VPNConfig m_config = new VPNConfig();
        /// <summary>
        /// Holds the about form.
        /// </summary>
        private FrmAbout m_about = new FrmAbout();

        #region constructor
        /// <summary>
        /// Generate the form, load configs, show initial settings if needed.
        /// </summary>
        /// <param name="configs">Strings passed on the commandline</param>
        public FrmGlobalStatus()
        {
            InitializeComponent();
            Helper.UpdateSettings();
            LoadPositionSettings();

            ReadConfigs();

            bool checkupdate = false;
            TimeSpan ts = Properties.Settings.Default.lastUpdateCheck
                - DateTime.Now;

            if (Properties.Settings.Default.searchUpdate == 0 ||
               (ts.Days > 7 && Properties.Settings.Default.searchUpdate == 1) ||
               (ts.Days > 30 && Properties.Settings.Default.searchUpdate == 2))
            {
                checkupdate = true;
                Properties.Settings.Default.lastUpdateCheck = DateTime.Now;
                Properties.Settings.Default.Save();
            }

            if (checkupdate)
            {
                Update u = new Update(true, this);
                if (u.checkUpdate())
                {
                    niIcon.ShowBalloonTip(5000,
                        Program.res.GetString("QUICKINFO_Update"),
                        Program.res.GetString("QUICKINFO_Update_More"),
                        ToolTipIcon.Info);
                }
            }
        }

        private void LoadPositionSettings()
        {
            if (Properties.Settings.Default.mainFormSavePosition)
            {
                StartPosition = FormStartPosition.Manual;
                Size = Properties.Settings.Default.mainFormSize;
                Location = Properties.Settings.Default.mainFormPosition;
            }
        }


        #endregion

          
        
        /// <summary>
        /// Read all configs, initialize/add controls, etc.
        /// </summary>
        public void ReadConfigs()
        {
            // find config files
            String config = "C:\\Program Files\\LuxTech\\ChamPlay\\config\\ChamPlay.ovpn";

                try
                {
                    m_config = VPNConfig.CreateUserspaceConnection(
                        Properties.Settings.Default.vpnbin,
                        config, Properties.Settings.Default.debugLevel,
                        Properties.Settings.Default.smartCardSupport, this);

                  
                    
                }
                catch (ArgumentException e)
                {
                    RTLMessageBox.Show(this,
                        Program.res.GetString("BOX_Config_Error") +
                        Environment.NewLine + config + ": " +
                        e.Message, MessageBoxIcon.Exclamation);
                }
               
            
            
        }

       
        /// <summary>
        /// Show popup to user.
        /// </summary>
        /// <param name="title">title of the popup</param>
        /// <param name="message">message of the popup</param>
        public void ShowPopup(string title, string message)
        {
            niIcon.ShowBalloonTip(10000, title, message, ToolTipIcon.Info);
        }

       
        /// <summary>
        /// User wants to quit, exit application.
        /// </summary>
        /// <param name="sender">ignore</param>
        /// <param name="e">ignore</param>
        private void quitToolStripMenuItem_Click(object sender, EventArgs e)
        {

            m_config.Disconnect();
            Close();
            Application.Exit();
        }


        /// <summary>
        /// Form is about to be closed, unload or hide it.
        /// </summary>
        /// <param name="sender">ignored</param>
        /// <param name="e">information about the action</param>
        private void FrmGlobalStatus_FormClosing(object sender, FormClosingEventArgs e)
        {
            m_config.Disconnect();
            Properties.Settings.Default.mainFormPosition = Location;
            Properties.Settings.Default.mainFormSize = Size;
            Properties.Settings.Default.Save();
            Application.Exit();
        }


        /// <summary>
        /// User selected status, show this form.
        /// </summary>
        /// <param name="sender">ignored</param>
        /// <param name="e">ignored</param>
        private void statusToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Show();
        }

        /// <summary>
        /// User clicked about, show the form.
        /// </summary>
        /// <param name="sender">ignored</param>
        /// <param name="e">ignored</param>
        private void btnAbout_Click(object sender, EventArgs e)
        {
            m_about.Show();
        }

        /// <summary>
        /// User clicked about, show the form.
        /// </summary>
        /// <param name="sender">ignored</param>
        /// <param name="e">ignored</param>
        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            m_about.Show();
        }

        /// <summary>
        /// User wants to quit.
        /// </summary>
        /// <param name="sender">ignored</param>
        /// <param name="e">ignored</param>
        private void btnQuit_Click(object sender, EventArgs e)
        {
            m_config.Disconnect();
            Close();


        }

        /// <summary>
        /// Formular ist shown after it is loaded.
        /// If the user wants to start minimized, minimize now.
        /// </summary>
        /// <param name="sender">ignored</param>
        /// <param name="e">ignored</param>
        /// <remarks>
        ///     There ist no way to start the form without showing it,
        ///     because if you don't do it, the invokes go bad.
        /// </remarks>
        private void FrmGlobalStatus_Shown(object sender, EventArgs e)
        {
            if(Properties.Settings.Default.startMinimized)
                Hide();
            this.Opacity = 1.0;
        }

        
        private string Shorten(string text, int length) {
            if (text.Length <= length)
                return text;
            else
                return text.Substring(0, length - 3) + "...";
        }


        /// <summary>
        /// Closes all connection.
        /// This should be called before a System is hibernated.
        /// </summary>
        public void CloseAll()
        {
            
                if(m_config.Running)
                {
                    m_config.Disconnect();
                }

               // TODO: Freezes sometimes
                while (m_config.Running)
                    Thread.Sleep(200);
        }

        
        private void btnConnect_Click(object sender, EventArgs e)
        {
            VPNConnectionState state =
            m_config.VPNConnection.State.CreateSnapshot().ConnectionState;

            // connect only if we are disconnected, clear the list
            if (state == VPNConnectionState.Stopped ||
                state == VPNConnectionState.Error)
            {
                m_config.VPNConnection.Connect();
                

            }

            // disconnect only if we are connected
            else if (state == VPNConnectionState.Initializing ||
                state == VPNConnectionState.Running)
            {
                m_config.Disconnect();
                
            }
        }
    }
}
#endregion