using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace SignalR.Fleck
{
    /// <summary>
    /// Helper class used to serve files
    /// </summary>
    public class FileServer
    {
        private readonly HttpListener _listener;
        private readonly string _basePath;
        private readonly string _url;

        public FileServer(string url, string basePath)
        {
            _url = url;
            _basePath = basePath;
            _listener = new HttpListener();
            _listener.Prefixes.Add(url);
        }

        public void Start()
        {
            Debug.WriteLine("{0} [FILE SERVER]: Started on {1}", DateTime.Now, _url);
            _listener.Start();
            ServeFiles();
        }
        
        public void Stop()
        {
            _listener.Stop();
        }

        private void ServeFiles()
        {
            _listener.BeginGetContext(ar =>
            {

                HttpListenerContext context;

                try
                {
                    context = _listener.EndGetContext(ar);
                }
                catch (Exception)
                {
                    return;
                }

                Debug.WriteLine("{0} [FILE SERVER]: Received request from {1}", DateTime.Now, context.Request.RawUrl);

                ServeFiles();

                try
                {
                    string path = MapPath(context.Request.RawUrl);


                    if (File.Exists(path))
                    {                        
                        var bytes = File.ReadAllBytes(path);
                        context.Response.ContentType = GetMimeType(Path.GetExtension(path));
                        context.Response.Close(bytes, true);
                    }
                    else
                    {
                        Debug.WriteLine("{0} [FILE SERVER]: Unable to find {1}", DateTime.Now, path);
                        context.Response.StatusCode = 404;
                        context.Response.StatusDescription = "Not Found";
                        context.Response.Close();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("{0} [FILE SERVER]: {1}", DateTime.Now, ex);
                    context.Response.StatusCode = 500;
                    context.Response.StatusDescription = "Server Error";
                    context.Response.Close(Encoding.UTF8.GetBytes(ex.ToString()), true);
                }

            },
            null);
        }

        private string MapPath(string url)
        {
            // Change the direction of the slashes so /foo/bar becomes foo\bar
            string path = url.Replace("/", "\\");

            // Replace / with the app root
            if (path.Length > 0 && path[0] == '\\')
            {
                path = path.Substring(1);
            }

            // Now combine it with the web root
            string fullPath = Path.Combine(_basePath, path);

            // Resolve the full path
            return Path.GetFullPath(fullPath);
        }

        public static string GetMimeType(string extension)
        {
            switch (extension)
            {
                case ".htm":
                case ".html":
                    return "text/html";
                case ".css":
                    return "text/css";
                default:
                    break;
            }

            return "text/plain";
        }        
    }
}
