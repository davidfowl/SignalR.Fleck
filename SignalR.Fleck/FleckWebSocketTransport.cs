using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fleck;
using SignalR.Abstractions;
using SignalR.Transports;

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
                    Connected().Then((conn, tcs) => ProcessMessages(null, conn, tcs), connection, taskCompletionSource);
                }
                else
                {
                    // Just raise the event if there's no handler
                    taskCompletionSource.SetResult(null);
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

        public Func<string, Task> Received { get; set; }

        public Task Send(object value)
        {
            return TaskAsyncHelper.FromMethod((connection, data) => connection.Send(data),
                                              _webSocketConnection,
                                              _serializer.Stringify(value))
                                              .Catch();
        }

        private Task ProcessMessages(long? messageId, IReceivingConnection connection, TaskCompletionSource<object> tcs)
        {
            if (_disconnected)
            {
                tcs.SetResult(null);
            }
            else
            {
                Task<PersistentResponse> receiveTask = messageId.HasValue ?
                                                       connection.ReceiveAsync(messageId.Value) :
                                                       connection.ReceiveAsync();

                receiveTask.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        tcs.SetException(task.Exception);
                    }
                    else if (task.IsCanceled)
                    {
                        tcs.SetCanceled();
                    }
                    else
                    {
                        Send(task.Result).ContinueWith(innerTask =>
                        {
                            if (innerTask.IsFaulted)
                            {
                                tcs.SetException(innerTask.Exception);
                            }
                            else if (innerTask.IsCanceled)
                            {
                                tcs.SetCanceled();
                            }
                            else
                            {
                                ProcessMessages(task.Result.MessageId, connection, tcs);
                            }
                        });
                    }
                });
            }

            return tcs.Task;
        }
    }
}
