using System;
using System.Collections.Specialized;
using Fleck;
using SignalR.Abstractions;

namespace SignalR.Fleck
{
    public class FleckWebSocketRequest : IRequest
    {
        public FleckWebSocketRequest(IWebSocketConnectionInfo connectionInfo, bool secure)
        {
            Cookies = new NameValueCollection();
            Form = new NameValueCollection();
            Headers = new NameValueCollection();
            QueryString = new NameValueCollection();

            string[] hostInfo = connectionInfo.Host.Split(':');
            string host = hostInfo[0];
            int port = Int32.Parse(hostInfo[1]);
            string[] pathInfo = connectionInfo.Path.Split('?');
            string path = pathInfo[0];
            string query = pathInfo[1];

            Url = new UriBuilder()
            {
                Scheme = secure ? "wss" : "ws",
                Host = host,
                Port = port,
                Path = path,
                Query = query
            }.Uri;

            ParseQuery(query);

            foreach (var cookie in connectionInfo.Cookies)
            {
                Cookies[cookie.Key] = cookie.Value;
            }
        }

        private void ParseQuery(string query)
        {
            string[] pairs = query.Split(new[] { "&" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in pairs)
            {
                string[] entry = pair.Split('=');
                if (entry.Length > 1)
                {
                    QueryString.Add(entry[0], UrlDecode(entry[1]));
                }
            }
        }

        public NameValueCollection Cookies
        {
            get;
            private set;
        }

        public NameValueCollection Form
        {
            get;
            private set;
        }

        public NameValueCollection Headers
        {
            get;
            private set;
        }

        public NameValueCollection QueryString
        {
            get;
            private set;
        }

        public Uri Url
        {
            get;
            private set;
        }

        private static string UrlDecode(string url)
        {
            // HACK: Uri.UnescapeDataString doesn't seem to handle +
            // TODO: Copy impl from System.Web.HttpUtility.UrlDecode
            return Uri.UnescapeDataString(url).Replace("+", " ");
        }
    }
}
