﻿using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Extensions;
using TwitchLib.Communication.Helpers;

namespace TwitchLib.Communication.Services
{

    /// <summary>
    ///     <see langword="class"/> to bundle Network-Service-<see cref="Task"/>s
    /// </summary>
    internal class NetworkServices
    {
        #region properties private: Tasks/Timers
        // each Task is held in its own variable to be more precise

        /// <summary>
        ///     get is never used, cause the <see cref="Task"/> is canceled by the <see cref="Token"/>
        /// </summary>
        [SuppressMessage("Style", "IDE0052")]
        private Task ListenTask { get; set; }
        private Task MonitorTask { get; set; }
        #endregion properties private: Tasks


        #region properties private
        private ILogger LOGGER { get; }
        private ThrottlerService Throttler { get; }
        private CancellationToken Token => Client.Token;
        private AClientBase Client { get; }
        #endregion properties private


        #region properties internal
        internal ConnectionWatchDog ConnectionWatchDog { get; }
        #endregion properties internal


        #region ctors
        internal NetworkServices(AClientBase client,
                                 ThrottlerService throttler,
                                 ILogger logger = null)
        {
            LOGGER = logger;
            Client = client;
            Throttler = throttler;
            ConnectionWatchDog = new ConnectionWatchDog(Client, logger);
        }
        #endregion ctors


        #region methods internal
        internal void Start()
        {
            LOGGER?.TraceMethodCall(GetType());
            if (MonitorTask == null || !TaskHelper.IsTaskRunning(MonitorTask))
            {
                // this task is probably still running
                // may be in case of a network connection loss
                // all other Tasks havent been started or have been canceled!
                // ConnectionWatchDog is the only one, that has a seperate CancellationTokenSource!
                MonitorTask = ConnectionWatchDog.StartMonitorTask();
            }
            Throttler.Start();
            ListenTask = Task.Run(Client.ListenTaskAction, Token);
        }
        #endregion methods internal


        #region methods private

        internal void Stop()
        {
            LOGGER?.TraceMethodCall(GetType());
            Throttler.Stop();
            ConnectionWatchDog.Stop();
        }
        #endregion methods private
    }
}