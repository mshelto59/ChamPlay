using System;
using System.IO;
using System.Threading;
using OpenVPNUtils.States;

namespace OpenVPNUtils
{
    /// <summary>
    /// Provides access to OpenVPN.
    /// </summary>
    public class UserSpaceConnection : Connection, IDisposable
    {

        #region variables
        /// <summary>
        /// The OpenVPN binary service.
        /// </summary>
        private UserSpaceService m_ovpnService;

        /// <summary>
        /// Counts, how many objects have been created.
        /// </summary>
        private static int obj_count;

        /// <summary>
        /// Holds information if the log file should deleted at the end
        /// </summary>
        private bool m_deleteLogFile;

        private int m_connectState;
        private bool m_abort;

        private object lockvar = new Object();
        #endregion

        #region constructors/destructors
        /// <summary>
        /// Initializes a new OVPN Object.
        /// Also set a LogEventDelegate so that the first log lines are reveived.
        /// </summary>
        /// <param name="bin">Path to openvpn binary</param>
        /// <param name="config">Path to configuration file</param>
        /// <param name="earlyLogLevel">Log level</param>
       public UserSpaceConnection(string bin, string config,
           int earlyLogLevel)
        {
            if (bin == null || bin.Length == 0)
                throw new ArgumentNullException(bin, "OpenVPN Binary is not valid/selected");
            if (config == null || config.Length == 0)
                throw new ArgumentNullException(config, "Config file is not valid/selected");
            if (!new FileInfo(bin).Exists)
                throw new FileNotFoundException(bin,
                    "Binary \"" + bin + "\" does not exist");
            if (!new FileInfo(config).Exists)
                throw new FileNotFoundException(config,
                    "Config file \"" + config + "\" does not exist");

            String logFile = getLogFile(config);
            String forwardLogFile;
            if (logFile == null)
            {
                forwardLogFile = Path.GetTempFileName();
                m_logFile = forwardLogFile;
                m_deleteLogFile = true;
            }
            else
            {
                forwardLogFile = null;
                m_logFile = logFile;
                m_deleteLogFile = false;
            }

            this.Init("127.0.0.1", 11195 + obj_count++, earlyLogLevel, true);
            m_ovpnService = new UserSpaceService(bin, config,
                Path.GetDirectoryName(config), base.Host, base.Port,
                forwardLogFile);

            m_ovpnService.serviceExited += new EventHandler(m_ovpnService_serviceExited);
        }

        /// <summary>
        /// Destructor. Terminates a remaining connection.
        /// </summary>
        ~UserSpaceConnection()
        {
            Dispose(false);
        }
        #endregion

        #region eventhandler
        /// <summary>
        /// If the service exits, disconnect, so we got a propper state.
        /// </summary>
        /// <param name="sender">ignored</param>
        /// <param name="e">ignored</param>
        void m_ovpnService_serviceExited(object sender, EventArgs e)
        {
            try
            {
                if (State.ConnectionState != VPNConnectionState.Stopping
                    && State.ConnectionState != VPNConnectionState.Stopped)
                {
                    Disconnect();
                    State.ChangeState(VPNConnectionState.Error);
                    IP = null;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
        #endregion

        /// <summary>
        /// Connects with the configured parameters.
        /// </summary>
        /// <seealso cref="Disconnect"/>
        public override void Connect()
        {
            CheckState(VPNConnectionState.Initializing);
            State.ChangeState(VPNConnectionState.Initializing);

            m_connectState = 1;
            m_abort = false;

            m_ovpnService.Start();
            if (!m_ovpnService.isRunning)
            {
                State.ChangeState(VPNConnectionState.Error);
                IP = null;
                return;
            }

            UtilsHelper.Function<bool> cld = new UtilsHelper.Function<bool>(ConnectLogic);
            m_connectState = 2;
            cld.BeginInvoke(connectComplete, cld);
        }

        private void connectComplete(IAsyncResult result)
        {
            StateSnapshot ss;
            UtilsHelper.Function<bool> callback;
            bool abort;
            int connectionState;

            try
            {
                Monitor.Enter(lockvar);
                ss = State.CreateSnapshot();
                callback = (UtilsHelper.Function<bool>)result.AsyncState;
                abort = m_abort;
                connectionState = m_connectState;

                if (callback.EndInvoke(result))
                    m_connectState = 3;
            }
            finally
            {
                Monitor.Exit(lockvar);
            }


            if (abort && ss.ConnectionState == VPNConnectionState.Stopping)
            {
                m_abort = false;
                
                switch (connectionState)
                {
                    case 1: // service not startet
                       
                        break;
                    case 2: // service startet, not connected via tcp
                       
                        m_ovpnService.kill();
                        break;
                    case 3: // service startet and connected via tcp
                       
                        Disconnect();
                        break;
                    default:
                        
                        break;
                }
                State.ChangeState(VPNConnectionState.Stopped);
            }
        }

        /// <summary>
        /// Disconnects from the OpenVPN Service.
        /// </summary>
        /// <seealso cref="Connect"/>
        public override void Disconnect()
        {
            StateSnapshot ss;

            try
            {
                Monitor.Enter(lockvar);
                if (State.ConnectionState == VPNConnectionState.Stopped) return;

                if (State.ConnectionState == VPNConnectionState.Error)
                {
                    State.ChangeState(VPNConnectionState.Stopped);
                    return;
                }

                ss = State.ChangeState(VPNConnectionState.Stopping);
                m_abort = true;


                if (ss.ConnectionState == VPNConnectionState.Running ||
                    (ss.ConnectionState == VPNConnectionState.Initializing &&
                    m_connectState == 3))
                {
                    Logic.sendQuit();
                    Thread t = new Thread(new ThreadStart(killtimer));
                    t.Name = "async disconnect thread";
                    t.Start();
                    m_abort = false;
                }
            }
            finally
            {
                Monitor.Exit(lockvar);
            }
        }

        /// <summary>
        /// Kill the connection after 30 secons unless it is closed
        /// </summary>
        private void killtimer()
        {
            while (Logic.isConnected())
                Thread.Sleep(100);
            DisconnectLogic();

            // wait 30 seconds, stop if the service is down
            for (int i = 0; i < 60; ++i)
            {
                if (!m_ovpnService.isRunning)
                    break;
                Thread.Sleep(500);
            }

            if (m_ovpnService.isRunning)
            {
                m_ovpnService.kill();
            }

            State.ChangeState(VPNConnectionState.Stopped);
        }

        #region IDisposable Members

        private bool disposed;

        /// <summary>
        /// Dispose this object.
        /// </summary>
        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose this object.
        /// </summary>
        /// <param name="disposing">true if called from Dispose(), false if called from destructor</param>
        private void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (m_deleteLogFile)
                {
                    File.Delete(m_logFile);
                }

                base.Dispose();
                if (disposing)
                {
                    m_ovpnService.Dispose();
                }

                m_ovpnService = null;
                disposed = true;
            }
        }

        #endregion
    }
}