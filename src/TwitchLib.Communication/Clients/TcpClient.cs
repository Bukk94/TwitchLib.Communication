﻿using System;
using System.IO;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchLib.Communication.Events;
using TwitchLib.Communication.Extensions;
using TwitchLib.Communication.Interfaces;

namespace TwitchLib.Communication.Clients
{
    public class TcpClient : ClientBase<System.Net.Sockets.TcpClient>
    {
        private StreamReader _reader;
        private StreamWriter _writer;
        
        protected override string Url => "irc.chat.twitch.tv";

        private int Port => Options.UseSsl ? 6697 : 6667;

        public override bool IsConnected => Client?.Connected ?? false;

        public TcpClient(
            IClientOptions options = null,
            ILogger logger = null) 
            : base(options, logger)
        {
        }

        internal override async Task ListenTaskActionAsync()
        {
            Logger?.TraceMethodCall(GetType());
            if (_reader == null)
            {
                var ex = new InvalidOperationException($"{nameof(_reader)} was null!");
                Logger?.LogExceptionAsError(GetType(), ex);
                RaiseFatal(ex);
                throw ex;
            }

            while (IsConnected)
            {
                try
                {
                    var input = await _reader.ReadLineAsync();
                    if (input is null)
                    {
                        continue;
                    }

                    RaiseMessage(new OnMessageEventArgs { Message = input });
                }
                catch (Exception ex) when (ex.GetType() == typeof(TaskCanceledException) ||
                                           ex.GetType() == typeof(OperationCanceledException))
                {
                    // occurs if the Tasks are canceled by the CancellationTokenSource.Token
                    Logger?.LogExceptionAsInformation(GetType(), ex);
                }
                catch (Exception ex)
                {
                    Logger?.LogExceptionAsError(GetType(), ex);
                    RaiseError(new OnErrorEventArgs { Exception = ex });
                    break;
                }
            }
        }

        protected override async Task ClientSendAsync(string message)
        {
            Logger?.TraceMethodCall(GetType());

            // this is not thread safe
            // this method should only be called from 'ClientBase.Send()'
            // where its call gets synchronized/locked
            // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream?view=netstandard-2.0#remarks
            if (_writer == null)
            {
                var ex = new InvalidOperationException($"{nameof(_writer)} was null!");
                Logger?.LogExceptionAsError(GetType(), ex);
                RaiseFatal(ex);
                throw ex;
            }

            await _writer.WriteLineAsync(message);
            await _writer.FlushAsync();
        }

        protected override async Task ConnectClientAsync()
        {
            Logger?.TraceMethodCall(GetType());
            if (Client == null)
            {
                Exception ex = new InvalidOperationException($"{nameof(Client)} was null!");
                Logger?.LogExceptionAsError(GetType(), ex);
                throw ex;
            }

            try
            {
                // https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios
#if NET6_0_OR_GREATER
            // within the following thread:
            // https://stackoverflow.com/questions/4238345/asynchronously-wait-for-taskt-to-complete-with-timeout
            // the following answer
            // NET6_0_OR_GREATER: https://stackoverflow.com/a/68998339

            var connectTask = Client.ConnectAsync(Url, Port);
            var waitTask = connectTask.WaitAsync(TimeOutEstablishConnection, Token);
            await Task.WhenAny(connectTask, waitTask);
#else
                // within the following thread:
                // https://stackoverflow.com/questions/4238345/asynchronously-wait-for-taskt-to-complete-with-timeout
                // the following two answers:
                // https://stackoverflow.com/a/11191070
                // https://stackoverflow.com/a/22078975
                
                using (var delayTaskCancellationTokenSource = new CancellationTokenSource())
                {
                    var connectTask = Client.ConnectAsync(Url, Port);
                    var delayTask = Task.Delay(
                        (int)TimeOutEstablishConnection.TotalMilliseconds,
                        delayTaskCancellationTokenSource.Token);
                    
                    await Task.WhenAny(connectTask, delayTask);
                    delayTaskCancellationTokenSource.Cancel();
                }
#endif
                if (!Client.Connected)
                {
                    Logger?.TraceAction(GetType(), "Client couldn't establish connection");
                    return;
                }

                Logger?.TraceAction(GetType(), "Client established connection successfully");
                if (Options.UseSsl)
                {
                    var ssl = new SslStream(Client.GetStream(), false);
                    await ssl.AuthenticateAsClientAsync(Url);
                    _reader = new StreamReader(ssl);
                    _writer = new StreamWriter(ssl);
                }
                else
                {
                    _reader = new StreamReader(Client.GetStream());
                    _writer = new StreamWriter(Client.GetStream());
                }
            }
            catch (Exception ex) when (ex.GetType() == typeof(TaskCanceledException) ||
                                       ex.GetType() == typeof(OperationCanceledException))
            {
                // occurs if the Tasks are canceled by the CancellationTokenSource.Token
                Logger?.LogExceptionAsInformation(GetType(), ex);
            }
            catch (Exception ex)
            {
                Logger?.LogExceptionAsError(GetType(), ex);
            }
        }

        protected override System.Net.Sockets.TcpClient CreateClient()
        {
            Logger?.TraceMethodCall(GetType());

            return new System.Net.Sockets.TcpClient
            {
                // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.tcpclient.lingerstate?view=netstandard-2.0#remarks
                LingerState = new System.Net.Sockets.LingerOption(true, 0)
            };
        }

        protected override void CloseClient()
        {
            Logger?.TraceMethodCall(GetType());
            _reader?.Dispose();
            _writer?.Dispose();
            Client?.Dispose();
        }
    }
}