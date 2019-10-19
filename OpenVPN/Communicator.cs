﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace OpenVPNUtils
{
    internal class Communicator : IDisposable
    {
        #region Variables
        /// <summary>
        /// Host to communicate with (normally '127.0.0.1').
        /// </summary>
        private string m_host;

        /// <summary>
        /// Port to communicate with.
        /// </summary>
        private int m_port;

        /// <summary>
        /// TCP Client which is connected to <c>m_host:m_port</c>.
        /// Used to send and Receive Data to/from the OpenVPN Management Interface.
        /// </summary>
        private TcpClient m_tcpC;

        /// <summary>
        /// Streamreader used to read from <c>m_tcpC</c>.
        /// </summary>
        private StreamReader m_sread;

        /// <summary>
        /// Streamwriter used to write to <c>m_tcpC</c>.
        /// </summary>
        private StreamWriter m_swrite;

        /// <summary>
        /// Thread to read from <c>m_sread</c> asynchronly.
        /// </summary>
        private Thread m_reader;


        /// <summary>
        /// Saves whether the Object is connected to a OpenVPN Instance.
        /// </summary>
        private bool m_connected;
        #endregion

        #region events
        /// <summary>
        /// OVPNCommunicator received a line.
        /// </summary>
        public event UtilsHelper.Action<object, GotLineEventArgs> gotLine;

        /// <summary>
        /// Server closed the connection.
        /// </summary>
        public event EventHandler connectionClosed;

        #endregion

        #region Constructor
        /// <summary>
        /// Initializes a new OVPNCommunicator object.
        /// </summary>
        /// <param name="host">Host to connect to (127.0.0.1)</param>
        /// <param name="port">Port to connect to</param>
        
        public Communicator(string host, int port)
        {
            m_host = host;
            m_port = port;
            m_tcpC = new TcpClient();
        }
        #endregion

        /// <summary>
        /// Connects to the management interface.
        /// </summary>
        public void connect()
        {
            
            try
            {
                m_tcpC.Connect(m_host, m_port);
            }
            catch (SocketException e)
            {
                
                throw; // new ApplicationException("Could not connect to socket: " + e.Message);
            }
            
            m_sread = new StreamReader(m_tcpC.GetStream());
            m_swrite = new StreamWriter(m_tcpC.GetStream());
            m_reader = new Thread(new ThreadStart(readerThread));
            m_reader.Name = "management interface reader thread";
            m_connected = true;
            m_reader.Start();
        }

        public void processManagementConnectionLine()
        {
            // read a line
            string line = m_sread.ReadLine();
            if (line == null)
                throw new IOException("Got null");

            // log line, fire event
            gotLine(this, new GotLineEventArgs(line));
        }

        /// <summary>
        /// Reads lines from the connection, fires events.
        /// </summary>
        private void readerThread()
        {
            try
            {
                // read until...
                while (true)
                {
                    processManagementConnectionLine();
                }
            }

            // thread was aborted (this happens on disconnection)
            catch (ThreadAbortException)
            {
               
            }

            // ioexception (this can happen on disconnection, too)
            catch (IOException e)
            {
                
            }

           
            m_connected = false;
            
            if(connectionClosed != null)
                connectionClosed(this, new EventArgs());
        }

        /// <summary>
        /// Sends a string to the management interface.
        /// </summary>
        /// <param name="s">The string to send</param>
        public bool send(string s)
        {
            bool ret = false;

            if(m_connected)
            {
                lock (m_swrite)
                {
                   

                    try
                    {
                        m_swrite.WriteLine(s);
                        m_swrite.Flush();
                        ret = true;
                    }
                    catch (IOException)
                    {
                      
                    }
                }
            }


              

            return ret;
        }

        /// <summary>
        /// Determines whether we are connected.
        /// </summary>
        /// <returns>true if the socket is connected, false otherwise</returns>
        public bool isConnected()
        {
            return m_connected;
        }

        /// <summary>
        /// Close all readers, writers, stop thread, close connection imediatelly.
        /// </summary>
        public void disconnect()
        {
            

            if (m_reader != null && !Thread.CurrentThread.Equals(m_reader))
                m_reader.Abort();

            if(m_sread != null)
                m_sread.Close();

            if(m_swrite != null)
                m_swrite.Close();
            m_tcpC = new TcpClient();
            m_tcpC.Close();
           
        }

        #region IDisposable Members

        ~Communicator()
        {
            disconnect();
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                disconnect();
                if(m_sread != null) m_sread.Dispose();
                if(m_swrite != null) m_swrite.Dispose();
                if(m_tcpC != null) m_tcpC.Close();
            }

            m_sread = null;
            m_swrite = null;
            m_tcpC = null;
        }

        #endregion
    }
}