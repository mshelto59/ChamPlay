using System;
using System.Collections.Generic;
using System.Threading;
using OpenVPNUtils.States;

namespace OpenVPNUtils
{
    /// <summary>
    /// The logic which decides what to send to the management interface
    /// and what to ask the user for, and when to do it.
    /// </summary>
    internal class ManagementLogic : IDisposable
    {
        #region enums
        /// <summary>
        /// Structure that tells what we are waiting for.
        /// </summary>
        public enum WaitState
        {
            /// <summary>
            /// We are waiting for nothing.
            /// </summary>
            NULL,

            /// <summary>
            /// We requested the number of found SmartCards.
            /// </summary>
            PKCS11_GET_COUNT,

            /// <summary>
            /// We requested a SmartCard key.
            /// </summary>
            PKCS11_GET_KEYS,

            /// <summary>
            /// We sent "log on".
            /// </summary>
            LOG_ON_ALL_1,

            /// <summary>
            /// After a "log on", we received a "ok".
            /// </summary>
            LOG_ON_ALL_2,

            /// <summary>
            /// After a "log on" and a "ok", we received the last log lines.
            /// </summary>
            LOG_ON,

            /// <summary>
            /// We requested a hold release.
            /// </summary>
            HOLD_RELEASE,

            /// <summary>
            /// We requested "state"
            /// </summary>
            STATE,

            /// <summary>
            /// We sent a signal
            /// </summary>
            SIGNAL
        }
        #endregion

        #region variables
        /// <summary>
        /// The parser used to "read" the answer.
        /// </summary>
        private ManagementParser m_ovpnMParser;

        /// <summary>
        /// The communicator used to read and write from/to 
        /// the management interface of OpenVPN.
        /// </summary>
        private Communicator m_ovpnComm;

        /// <summary>
        /// What are we waiting for at the moment?
        /// </summary>
        private WaitState m_state = WaitState.NULL;

        /// <summary>
        /// Asyncronous events to process.
        /// </summary>
        private List<AsyncEventDetail> m_todo = new List<AsyncEventDetail>();

        /// <summary>
        /// Parent.
        /// </summary>
        private Connection m_ovpn;

        /// <summary>
        /// If openvpn is locked - should we release it?
        /// </summary>
        private bool m_releaselock;

        /// <summary>
        /// Shall old log entries be received, too?
        /// </summary>
        private bool m_receiveOldLogs;
        #endregion

        #region constructor
        /// <summary>
        /// Creates a new ManagementLogic object.
        /// </summary>
        /// <param name="ovpn">parent</param>
        /// <param name="host">host to connect to (e.g. 127.0.0.1)</param>
        /// <param name="port">port to connect to</param>
        /// <param name="receiveOldLogs">Should old log lines be received?</param>
        public ManagementLogic(Connection ovpn, string host,
            int port, bool receiveOldLogs)
        {
            m_ovpn = ovpn;

            m_releaselock = true;
            m_receiveOldLogs = receiveOldLogs;

            // initialize required components
            m_ovpnComm = new Communicator(host, port);
            m_ovpnMParser = new ManagementParser(m_ovpnComm, this);

            m_ovpnComm.connectionClosed += new System.EventHandler(m_ovpnComm_connectionClosed);
        }
        #endregion

        /// <summary>
        /// The communication was closed. This indicates an error
        /// </summary>
        /// <param name="sender">ignored</param>
        /// <param name="e">ignored</param>
        void m_ovpnComm_connectionClosed(object sender, System.EventArgs e)
        {
            reset();
            if (m_ovpn.State.ConnectionState != VPNConnectionState.Stopping)
                m_ovpn.error();
        }

        /// <summary>
        /// Connects to the management interface.
        /// </summary>
        public void connect()
        {
            m_ovpnComm.connect();
        }

        /// <summary>
        /// Send OpenVPN a signal to quit and wait for answer.
        /// After returning from this method you can savely close all sockets by calling disconnect().
        /// </summary>
        public void sendQuit()
        {
            if (isConnected())
            {
                setLock(WaitState.SIGNAL);
                m_releaselock = false;
         
                m_ovpnComm.send("signal SIGTERM");
                while (m_state == WaitState.SIGNAL && m_ovpnComm.isConnected())
                {
                    Thread.Sleep(100);
                    m_ovpnComm.send("signal SIGTERM"); // HACK: <- this is crazy. TODO: find out, why this is needed.
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Send OpenVPN a signal to restart and wait for answer.
        /// </summary>
        public void sendRestart()
        {
            if (isConnected())
            {
                setLock(WaitState.SIGNAL);
                m_releaselock = false;
               
                m_ovpnComm.send("signal SIGHUP");
                while (m_state == WaitState.SIGNAL)
                {
                    Thread.Sleep(100);
                    /*
                     * HACK: If I got it correctly, there is a problem with some
                     * OpenVPN versions which throw away the second message if they
                     * got two messages in a small time window.
                     */
                    if (!m_ovpnComm.send("signal SIGHUP")) break;
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// Tell the Management interface that we want to disconnect.
        /// After returning from this method you can savely close all sockets by calling disconnect().
        /// </summary>
        public void sendDisconnect()
        {
           
            m_ovpnComm.send("exit");
            while (isConnected())
            {
                Thread.Sleep(100);
                /*
                 * HACK: If I got it correctly, there is a problem with some
                 * OpenVPN versions which throw away the second message if they
                 * got two messages in a small time window.
                 */
                if (!m_ovpnComm.send("exit")) break;
                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// Closes the underlying connection.
        /// </summary>
        public void disconnect()
        {
            m_ovpnComm.disconnect();
        }

        /// <summary>
        /// Resets internal state.
        /// </summary>
        public void reset()
        {
            

            if (m_todo != null)
                m_todo.Clear();

            if (m_ovpnMParser != null)
                m_ovpnMParser.reset();

            m_state = WaitState.NULL;
            m_releaselock = true;
        }

        /// <summary>
        /// Returns whether the management logic is connected
        /// </summary>
        /// <returns>true if the logic is connected, false otherwise</returns>
        public bool isConnected()
        {
            return m_ovpnComm.isConnected();
        }

        /// <summary>
        /// Acquires a lock. Waits, until the lock is possible.
        /// </summary>
        /// <param name="newState">state to set</param>
        private void setLock(WaitState newState)
        {
            setLock(newState, false);
        }

        /// <summary>
        /// Acquires a lock.
        /// </summary>
        /// <param name="newState">state to set</param>
        /// <param name="force">if true, don't wait for the lock but take it. This can be a risk!</param>
        private void setLock(WaitState newState, bool force)
        {
            if (force)
            {
                m_state = newState;
                return;
            }

            while (true)
            {
                try
                {
                    Monitor.Enter(m_todo);
                    if (m_state == WaitState.NULL)
                    {
                        m_state = newState;
                        return;
                    }
                }
                finally
                {
                    Monitor.Exit(m_todo);
                }

                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// Release the acquired lock.
        /// </summary>
        private void releaseLock()
        {
            try
            {
                Monitor.Enter(m_todo);
                m_state = WaitState.NULL;
            }
            finally
            {
                Monitor.Exit(m_todo);
            }

            if (m_todo.Count > 0)
            {
                AsyncEventDetail e;
                try
                {
                    Monitor.Enter(m_todo);
                    e = m_todo[0];
                    m_todo.Remove(e);
                }
                finally
                {
                    Monitor.Exit(m_todo);
                }

                executeAsyncEvent(e);
            }
        }

        /// <summary>
        /// We got a synchronous message.
        /// </summary>
        /// <param name="msg">The message</param>
        public void cb_syncEvent(string msg)
        {
            // the reaction depends on what we are waiting for
            switch (m_state)
            {
                // logging was turned on, wait for log lines
                case WaitState.LOG_ON_ALL_1:
                    setLock(WaitState.LOG_ON_ALL_2, true);
                    break;

                // logging was turned on
                case WaitState.LOG_ON:
                case WaitState.LOG_ON_ALL_2:
                    ProcessSyncEventLogOnAll(msg);
                    break;

                // "state" was set
                case WaitState.STATE:
                    ProcessSyncEventState();
                    break;

                // hold relese was executed
                case WaitState.HOLD_RELEASE:
                    releaseLock();
                    break;

                // we sent a signal
                case WaitState.SIGNAL:
                    releaseLock();
                    break;

                // something else happened (this should not happen)
                // we release the lock
                default:
                    releaseLock();
                    break;
            }
        }

        private void ProcessSyncEventState()
        {
            releaseLock();
            setLock(WaitState.HOLD_RELEASE);
            m_ovpnComm.send("hold release");
        }

        private void ProcessSyncEventLogOnAll(string msg)
        {
            releaseLock();

            string[] m = msg.Split("\n".ToCharArray());
            for (int i = 0; i < m.GetUpperBound(0) - 1; ++i)
                addLog(m[i]);

            setLock(WaitState.STATE);
            m_ovpnComm.send("state on");
        }

       
        /// <summary>
        /// We got an asynchronous event (e.g. a log message).
        /// </summary>
        /// <param name="aeDetail">details about the event</param>
        public void got_asyncEvent(AsyncEventDetail aeDetail)
        {
           

            // if we can't execute just queue it
            try
            {
                Monitor.Enter(m_todo);
                if (m_state != WaitState.NULL)
                {
                    m_todo.Add(aeDetail);
                    return;
                }
            }
            finally
            {
                Monitor.Exit(m_todo);
            }
            executeAsyncEvent(aeDetail);
        }

        private void executeAsyncEvent(AsyncEventDetail aeDetail) {
            switch (aeDetail.eventType)
            {
                case AsyncEventDetail.EventType.NEEDSTR:
                    ProcessAsyncEventNeedStr(aeDetail);
                    break;

                // a password is requested
                case AsyncEventDetail.EventType.PASSWORD:
                    ProcessAsyncEventPassword(aeDetail);
                    break;

                // a hold state is signalized
                case AsyncEventDetail.EventType.HOLD:
                    ProcessAsyncEventHold(aeDetail);
                    break;

                case AsyncEventDetail.EventType.INFO:
                    break;

                // the internal state changed
                case AsyncEventDetail.EventType.STATE:
                    ProcessAsyncEventState(aeDetail);
                    break;

                // we got a "log"
                case AsyncEventDetail.EventType.LOG:
                    ProcessAsyncEventLog(aeDetail);
                    break;
            }
        }

        private void ProcessAsyncEventLog(AsyncEventDetail aeDetail)
        {
            addLog(aeDetail.message);
        }

        private void ProcessAsyncEventState(AsyncEventDetail aeDetail)
        {
            string[] details = aeDetail.getInfos();

            // otherwise, we automatically reconnect
            m_ovpn.State.ChangeVPNState(details);

           
                
        }

        private void ProcessAsyncEventHold(AsyncEventDetail aeDetail)
        {
            // it is released
            if (aeDetail.message.Contains("Waiting") && m_releaselock)
            {
                /* 
                 * enable logging
                 * (this is a trick to get all logs, because at the
                 * beginning, the hold state is set)
                 */

                if (m_receiveOldLogs)
                {
                    setLock(WaitState.LOG_ON_ALL_1);
                    m_ovpnComm.send("log on all");
                    m_receiveOldLogs = false;
                }
                else
                {
                    setLock(WaitState.LOG_ON_ALL_2);
                    m_ovpnComm.send("log on");
                }
            }
        }

        private void ProcessAsyncEventNeedStr(AsyncEventDetail aeDetail)
        {

            switch (aeDetail.getInfos()[0])
            {
                // A SmartCard ID is requested
                case "pkcs11-id-request":
                   

                    setLock(WaitState.PKCS11_GET_COUNT);
                    m_ovpnComm.send("pkcs11-id-count");
                    break;
            }
        }

        private void ProcessAsyncEventPassword(AsyncEventDetail aeDetail)
        {
            string pwType = aeDetail.getInfos()[0]; // "Auth" or "Private Key", or ...
            string pwInfo = aeDetail.getInfos()[1]; // "password" or "username/password"
            string pwMsg = aeDetail.getInfos()[2];  // "Need" or "Verification Failed"

            if (pwMsg.Equals("Need", System.StringComparison.OrdinalIgnoreCase))
            {
                if (pwType.Equals("Auth",
                    System.StringComparison.OrdinalIgnoreCase) &&
                    pwInfo.Equals("username/password",
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    // Ask for username/password
                    string[] loginInfo = m_ovpn.getLoginPass(pwType);
                    bool sendUserPass = false;
                    if (loginInfo != null)
                    {
                        string username = loginInfo[0];
                        string password = loginInfo[1];
                        if (username != null && pwType.Length > 0 &&
                            password != null && password.Length > 0)
                        {
                            m_ovpnComm.send("username '" + pwType + "' " +
                                ManagementParser.encodeMsg(username));
                            //wait for processing by OpenVPN or it might not always process the password correctly.
                            m_ovpnComm.processManagementConnectionLine();
                            m_ovpnComm.send("password '" + pwType + "' " +
                                ManagementParser.encodeMsg(password));
                            sendUserPass = true;
                        }
                    }
                    if (!sendUserPass)
                    {
                        // Send 'bogus' user and pass to keep OpenVPN from quiting on disconnect.. (WORKAROUND)
                        m_ovpnComm.send("username '" + pwType + "' -");
                        m_ovpnComm.send("password '" + pwType + "' -");
                    }

                }
                else
                {
                    string pw = m_ovpn.getPW(pwType);
                    if (pw != null)
                    {
                        if (pw.Length > 0)
                        {
                            m_ovpnComm.send("password '" + pwType + "' " +
                                ManagementParser.encodeMsg(pw));
                        }
                    }
                }
            }
            else if (pwMsg.Equals("Verification Failed",
                System.StringComparison.OrdinalIgnoreCase))
            {
             
            }
            else
            {
                
            }
        }

        private void addLog(string message)
        {
            string[] parts = message.Split(new char[] { ',' }, 3);
            long time;
            if (!long.TryParse(parts[0], out time))
                time = 0;

           
        }

        #region IDisposable Members

        private bool disposed;

        ~ManagementLogic()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    m_ovpnComm.Dispose();
                }
               
                m_ovpnMParser = null;
                m_todo = null;
                m_ovpn = null;
                disposed = true;
            }
        }

        #endregion
    }
}
