﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Events;
using TwitchLib.Communication.Extensions;

namespace TwitchLib.Communication.Services
{

    /// <summary>
    ///     just to check connection state
    /// </summary>
    [SuppressMessage("Style", "IDE0058")]
    internal class ConnectionWatchDog
    {

        #region properties private
        private ILogger LOGGER { get; }
        private AClientBase Client { get; }
        /// <summary>
        ///     <list>
        ///         <item>
        ///             should only be set to a new instance in <see cref="StartMonitorTask()"/>
        ///         </item>
        ///         <item>
        ///             should only be set to <see langword="null"/> in <see cref="Stop()"/>
        ///         </item>
        ///     </list>
        /// </summary>
        private CancellationTokenSource CancellationTokenSource { get; set; }
        private CancellationToken Token => (CancellationToken) (CancellationTokenSource?.Token);
        #endregion properties private


        #region ctors
        internal ConnectionWatchDog(AClientBase client,
                                    ILogger logger = null)
        {
            LOGGER = logger;
            Client = client;
        }
        #endregion ctors


        #region methods internal

        internal Task StartMonitorTask()
        {
            LOGGER?.TraceMethodCall(GetType());
            // we dont want to start more than one WatchDog
            if (CancellationTokenSource != null)
            {
                Exception ex = new InvalidOperationException("Monitor Task cant be started more than once!");
                LOGGER?.LogExceptionAsError(GetType(), ex);
                throw ex;
            }
            // this should be the only place where a new instance of CancellationTokenSource is set
            CancellationTokenSource = new CancellationTokenSource();
            if (Token == null)
            {
                Exception ex = new InvalidOperationException($"{nameof(Token)} was null!");
                LOGGER?.LogExceptionAsError(GetType(), ex);
                Client.RaiseFatal(ex);
                throw ex;
            }
            return Task.Run(MonitorTaskAction, Token);
        }

        internal void Stop()
        {
            LOGGER?.TraceMethodCall(GetType());
            CancellationTokenSource?.Cancel();
            CancellationTokenSource?.Dispose();
            // set it to null for the check within this.StartMonitorTask()
            CancellationTokenSource = null;
        }
        #endregion methods internal


        #region methods private
        private void MonitorTaskAction()
        {
            LOGGER?.TraceMethodCall(GetType());
            int millisecondsGone = 0;
            int delayInMilliseconds = 200;
            try
            {
                while (Token != null && !Token.IsCancellationRequested)
                {
                    // we expect the client is connected,
                    // when this monitor task starts
                    // cause ABaseClient.Open() starts networkservices after a connection could be established
                    if (!Client.IsConnected)
                    {
                        LOGGER?.TraceAction(GetType(), "Client isnt connected anymore");
                        // indicate, that the client isnt connected anymore
                        Client.RaiseStateChanged(new OnStateChangedEventArgs() { IsConnected = false, WasConnected = true });
                        // no call to close needed,
                        // ReconnectInternal() calls the correct Close-Method within the Client
                        // ReconnectInternal() makes attempts to reconnect according to the ReconnectionPolicy within the IClientOptions
                        LOGGER?.TraceAction(GetType(), "Try to reconnect");
                        bool connected = Client.ReconnectInternal();
                        if (!connected)
                        {
                            LOGGER?.TraceAction(GetType(), "Client couldnt reconnect");
                            // if the ReconnectionPolicy is set up to be finite
                            // and no connection could be established
                            // a call to Client.Close() is made
                            // that public Close() also shuts down this ConnectionWatchDog
                            Client.Close();
                            break;
                        }
                        LOGGER?.TraceAction(GetType(), "Client reconnected");
                        // otherwise the Client should be connected again
                        // and we indicate that StateChange
                        Client.RaiseStateChanged(new OnStateChangedEventArgs() { IsConnected = true, WasConnected = false });
                    }

                    //Check every 60s for Response
                    if (millisecondsGone >= 60_000)
                    {
                        LOGGER?.TraceAction(GetType(), "Send PING");
                        Client.SendPING();
                        millisecondsGone = 0;
                    }
                    Task.Delay(delayInMilliseconds).GetAwaiter().GetResult();
                    millisecondsGone += delayInMilliseconds;
                }
            }
            catch (Exception ex) when (ex.GetType() == typeof(TaskCanceledException) || ex.GetType() == typeof(OperationCanceledException))
            {
                // occurs if the Tasks are canceled by the CancelationTokenSource.Token
                LOGGER?.LogExceptionAsInformation(GetType(), ex);
            }
            catch (Exception ex)
            {
                LOGGER?.LogExceptionAsError(GetType(), ex);
                Client.RaiseError(new OnErrorEventArgs { Exception = ex });
                Client.RaiseFatal();

                // to ensure CancellationTokenSource is set to null again
                // call Stop();
                Stop();
            }
        }
        #endregion methods private
    }
}