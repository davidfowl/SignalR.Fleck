using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fleck;
using SignalR.Transports;
using SignalR.Hosting;

namespace SignalR.Fleck
{
    public class FleckWebSocketTransport : ITransport
    {
        private readonly HostContext _context;
        private readonly IJsonSerializer _serializer;
        private readonly IWebSocketConnection _webSocketConnection;
        private bool _disconnected;

        public FleckWebSocketTransport(HostContext context, IJsonSerializer serializer)
        {
            _context = context;
            _serializer = serializer;
            _webSocketConnection = context.GetValue<IWebSocketConnection>("Fleck.IWebSocketConnection");

            // Groups never come from the client
            Groups = Enumerable.Empty<string>();
        }

        public Func<Task> Connected { get; set; }

        public string ConnectionId
        {
            get { return _context.Request.QueryString["connectionId"]; }
        }

        public Func<Task> Disconnected { get; set; }

        public Func<Exception, Task> Error { get; set; }

        public IEnumerable<string> Groups
        {
            get;
            private set;
        }

        public Task ProcessRequest(IReceivingConnection connection)
        {
            // This will only be called on the first request so we return a task that fires on connect

            var taskCompletionSource = new TaskCompletionSource<object>();

            _webSocketConnection.OnOpen = () =>
            {
                if (Connected != null)
                {
                    TaskAsyncHelper.Interleave(ProcessMessages, Connected, connection).ContinueWith(taskCompletionSource);
                }
                else
                {
                    // Just process messages if there's no handler
                    ProcessMessages(connection).ContinueWith(taskCompletionSource);
                }
            };

            _webSocketConnection.OnClose = () =>
            {
                _disconnected = true;

                if (Disconnected != null)
                {
                    Disconnected().Catch();
                }
            };

            _webSocketConnection.OnError = ex =>
            {
                _disconnected = true;

                if (Error != null)
                {
                    Error(ex).Catch();
                }
            };

            _webSocketConnection.OnMessage = data =>
            {
                if (Received != null)
                {
                    Received(data).Catch();
                }
            };

            return taskCompletionSource.Task;
        }

        public Func<Task> Reconnected { get; set; }

        public Func<string, Task> Received { get; set; }

        public Task Send(object value)
        {
            return TaskAsyncHelper.FromMethod((connection, data) => connection.Send(data),
                                              _webSocketConnection,
                                              _serializer.Stringify(value))
                                              .Catch();
        }

        private Task ProcessMessages(IReceivingConnection connection, Action postReceive = null)
        {
            var tcs = new TaskCompletionSource<object>();
            ProcessMessages(null, connection, tcs, postReceive);
            return tcs.Task;
        }

        private void ProcessMessages(string messageId, IReceivingConnection connection, TaskCompletionSource<object> taskCompletionSource, Action postReceive = null)
        {
            if (_disconnected)
            {
                taskCompletionSource.SetResult(null);
            }
            else
            {
                Task<PersistentResponse> receiveTask = !String.IsNullOrEmpty(messageId) ?
                                                       connection.ReceiveAsync(messageId) :
                                                       connection.ReceiveAsync();

                if (postReceive != null)
                {
                    postReceive();
                }


                var receiveState = new
                {
                    Connection = connection,
                    Tcs = taskCompletionSource
                };

                receiveTask.Then(response => Send(response).Then((state, resp) => ProcessMessages(resp.MessageId, state.Connection, state.Tcs), receiveState, response))
                           .ContinueWith(task =>
                           {
                               if (task.IsCanceled)
                               {
                                   taskCompletionSource.SetCanceled();
                               }
                               else if (task.IsFaulted)
                               {
                                   taskCompletionSource.SetException(task.Exception);
                               }
                           },
                           TaskContinuationOptions.NotOnRanToCompletion);
            }
        }
    }
}
