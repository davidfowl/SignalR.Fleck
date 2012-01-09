using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Fleck;
using SignalR.Abstractions;
using SignalR.Infrastructure;
using SignalR.Samples.Raw;
using SignalR.SelfHost;
using SignalR.Transports;

namespace SignalR.Fleck
{
    class Program
    {
        static void Main(string[] args)
        {
            Debug.Listeners.Add(new ConsoleTraceListener());
            Debug.AutoFlush = true;
            FleckLog.Level = LogLevel.Debug;

            // Web socket server
            var wss = new WebSocketServer("ws://localhost:8181");

            // Main web server
            var server = new Server("http://localhost:8081/");

            // Helper file server
            var fileServer = new FileServer("http://localhost:8081/public/", @"..\..\www");

            // Hijack the negotiation request
            server.OnProcessRequest = hostContext =>
            {
                // The server supports websockets
                hostContext.Items[HostConstants.SupportsWebSockets] = true;

                // In negotiation, we tell the client the url of the web socket server for this connection
                hostContext.Items[HostConstants.WebSocketServerUrl] = wss.Location + hostContext.Request.Url.LocalPath.Replace("/negotiate", "");
            };

            wss.Start(socket =>
            {
                PersistentConnection connection;
                if (server.TryGetConnection(socket.ConnectionInfo.Path, out connection))
                {
                    var req = new FleckWebSocketRequest(socket.ConnectionInfo, wss.IsSecure);
                    var hostContext = new HostContext(req, null, null);

                    // Stack the socket in the items collection so the transport can use it
                    hostContext.Items["Fleck.IWebSocketConnection"] = socket;

                    try
                    {
                        connection.ProcessRequestAsync(hostContext).ContinueWith(task =>
                        {
                            Console.WriteLine(task.Exception.GetBaseException());
                        },
                        TaskContinuationOptions.OnlyOnFaulted);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }
                else
                {
                    socket.Close();
                }
            });


            // Register the websocket transport
            TransportManager.Default.Register("webSockets", GetFleckWebSocketTransport);

            server.MapConnection<Raw>("/raw");
            server.EnableHubs();

            server.Start();
            fileServer.Start();

            Process.Start("http://localhost:8081/public/raw/index.htm");

            Console.ReadKey();

            server.Stop();
            fileServer.Stop();
        }

        private static ITransport GetFleckWebSocketTransport(HostContext hostContext)
        {
            var serializer = DependencyResolver.Resolve<IJsonSerializer>();
            return new FleckWebSocketTransport(hostContext, serializer);
        }
    }
}
