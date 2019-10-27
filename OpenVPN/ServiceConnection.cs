using System;
using System.IO;
using OpenVPNUtils.States;
using System.Threading;
using OpenVPNUtils;

namespace OpenVPNUtils
{
    /// <summary>
    /// Provides access to OpenVPN.
    /// </summary>
    public class ServiceConnection : Connection
    {
        #region constructors/destructors
        /// <summary>
        /// Initializes a new OVPN Object.
        /// Also set a LogEventDelegate so that the first log lines are reveived.
        /// </summary>
        /// <param name="config">Path to configuration file</param>
        /// <param name="earlyLogEvent">Delegate to a event processor</param>
        /// <param name="earlyLogLevel">Log level</param>
        
        public ServiceConnection(string config,
            EventHandler<LogEventArgs> earlyLogEvent, int earlyLogLevel)
        {
            if (config == null)
                throw new ArgumentNullException(config, "Config file is null");
            if (!new FileInfo(config).Exists)
                throw new FileNotFoundException(config, "Config file \"" + config + "\" does not exist");

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
            var del = new UtilsHelper.Function<bool>(ConnectLogic);
            del.BeginInvoke(null, null);
        }

        /// <summary>
        /// Disconnects from the OpenVPN Service.
        /// </summary>
        /// <seealso cref="Connect"/>
        public override void Disconnect()
        {
            StateSnapshot ss = State.CreateSnapshot();
            if (ss.ConnectionState == VPNConnectionState.Stopped ||
                State.ConnectionState == VPNConnectionState.Error)
            {
                State.ChangeState(VPNConnectionState.Stopped);
                return;
            }
            State.ChangeState(VPNConnectionState.Stopping);

            var del = new UtilsHelper.Action(killConnection);
            del.BeginInvoke(null, null);
        }

        /// <summary>
        /// Kill the connection
        /// </summary>
        private void killConnection()
        {
            Logic.sendRestart();
            Logic.sendDisconnect();
            DisconnectLogic();
            State.ChangeState(VPNConnectionState.Stopped);
        }
    }
}