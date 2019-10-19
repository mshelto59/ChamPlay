﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;

namespace OpenVPNUtils
{
    /// <summary>
    /// controls a openvpn binary
    /// </summary>
    internal class UserSpaceService : IDisposable
    {
        #region variables
        /// <summary>
        /// information about the OpenVPN binary process start
        /// </summary>
        private ProcessStartInfo m_psi = new ProcessStartInfo();

        /// <summary>
        /// information about the OpenVPN binary process
        /// </summary>
        private Process m_process;

        /// <summary>
        /// saves, whether the OpenVPN Process is running
        /// </summary>
        private bool running;
        #endregion

        #region events
        /// <summary>
        /// fired when process closes
        /// </summary>
        public event EventHandler serviceExited;
        #endregion

        /// <summary>
        /// Initialize a new OpenVPN service.
        /// </summary>
        /// <param name="binfile">path to openvpn</param>
        /// <param name="configfile">path to openvpn config</param>
        /// <param name="dir">directory where config lies</param>
        /// <param name="logs">provider to write logs to</param>
        /// <param name="host">The host to connect to (e.g. 127.0.0.1)</param>
        /// <param name="port">The port to connect to</param>
        /// <param name="logfile">file to write OpenVPN log to</param>
        /// <param name="smartCardSupport">enable SmartCard support</param>
        public UserSpaceService(string binfile, string configfile, 
            string dir, string host, int port,
            string logfile, bool smartCardSupport)
        {
            

            m_psi.FileName = binfile;
            m_psi.WorkingDirectory = dir;
            m_psi.WindowStyle = ProcessWindowStyle.Hidden;
            m_psi.UseShellExecute = true;

            /*WindowsPrincipal principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            principal.IsInRole(WindowsBuiltInRole.Administrator)*/
            if (Environment.OSVersion.Version.Major >= 6)
                m_psi.Verb = "runas";

            m_psi.CreateNoWindow = true;
            m_psi.Arguments =
                (logfile != null ? "--log \"" + logfile + "\"" : "") +
                " --config \"" + configfile + "\"" +
                " --management " + host + " " + port.ToString(CultureInfo.InvariantCulture) +
                " --management-query-passwords" +
                " --management-hold" +
                " --management-signal" +
                " --management-forget-disconnect" +
                " --auth-retry interact";

            if (smartCardSupport)
                m_psi.Arguments += " --pkcs11-id-management";
        }

        /// <summary>
        /// Start the OpenVPN binary.
        /// </summary>
        public void Start() 
        {
           

            m_process = new Process();
            m_process.StartInfo = m_psi;
            m_process.Exited += new EventHandler(this.exited_event);
            m_process.EnableRaisingEvents = true;

            try
            {
                m_process.Start();
            } catch(System.ComponentModel.Win32Exception) {
               
                return;
            }

            
            running = true;
        }

        /// <summary>
        /// Kills the remaining process
        /// </summary>
        public void kill()
        {
            if (!running) return;
           

            try
            {
                m_process.Kill();
            }
            catch (InvalidOperationException e)
            {
              
            }
        }

        /// <summary>
        /// Reports the state of openvpn
        /// </summary>
        public bool isRunning
        {
            get { return running; }
        }

        /// <summary>
        /// Process exited, reset everything important.
        /// </summary>
        /// <param name="sender">ignored</param>
        /// <param name="args">ignored</param>
        private void exited_event(object sender, EventArgs args)
        {
            running = false;
            serviceExited(this, new EventArgs());
        }

        #region IDisposable Members

        /// <summary>
        /// Destructor. Disposes the object.
        /// </summary>
        private bool disposed;
        ~UserSpaceService()
        {
            Dispose(false);
        }

        /// <summary>
        /// Disposes the object.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the object.
        /// </summary>
        /// <param name="disposing">true if called from Dispose()</param>
        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                kill();
                if (disposing)
                {
                    m_process.Dispose();
                }

                m_process = null;
                disposed = true;
            }
        }

        #endregion
    }
}
