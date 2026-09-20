using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace DFMP.Runtime
{
    /// <summary>
    /// Catches Discord's redirect back to the player's own machine after they approve the connect in
    /// their browser.
    ///
    /// This is a raw <see cref="TcpListener"/> rather than <see cref="HttpListener"/> on purpose:
    /// HttpListener goes through http.sys on Windows, which refuses to reserve a prefix unless the
    /// process is elevated or an URL ACL was registered up front. A player running the game must not
    /// need either. Only one request is ever served, and only from loopback.
    /// </summary>
    public sealed class DFMPDiscordLoopbackListener : IDisposable
    {
        const int MaximumRequestBytes = 8 * 1024;

        TcpListener listener;

        public bool HasResult { get; private set; }
        public string Code { get; private set; } = string.Empty;
        public string Error { get; private set; } = string.Empty;
        public string State { get; private set; } = string.Empty;

        public bool Start(int port, out string error)
        {
            error = string.Empty;

            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch (SocketException ex)
            {
                error = $"could not listen on 127.0.0.1:{port} ({ex.SocketErrorCode}). " +
                    "Another program may already be using that port.";
                Stop();
                return false;
            }
            catch (Exception ex)
            {
                error = $"could not listen on 127.0.0.1:{port}: {ex.Message}";
                Stop();
                return false;
            }
        }

        /// <summary>
        /// Non-blocking. Call once per frame until <see cref="HasResult"/> becomes true.
        /// </summary>
        public void Poll()
        {
            if (HasResult || listener == null)
                return;

            try
            {
                if (!listener.Pending())
                    return;

                using (TcpClient client = listener.AcceptTcpClient())
                    ServeSingleRequest(client);
            }
            catch (Exception ex)
            {
                Error = "loopback redirect failed: " + ex.Message;
                HasResult = true;
            }
        }

        public void Fail(string error)
        {
            if (HasResult)
                return;

            Error = error ?? string.Empty;
            HasResult = true;
        }

        void ServeSingleRequest(TcpClient client)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;

            using (NetworkStream stream = client.GetStream())
            {
                string requestLine = ReadRequestLine(stream);
                string query = ExtractQuery(requestLine);
                Dictionary<string, string> values = ParseQuery(query);

                string code;
                values.TryGetValue("code", out code);
                string error;
                values.TryGetValue("error", out error);
                string state;
                values.TryGetValue("state", out state);

                Code = code ?? string.Empty;
                State = state ?? string.Empty;
                Error = error ?? string.Empty;

                if (Code.Length == 0 && Error.Length == 0)
                    Error = "discord redirect carried no authorization code";

                WriteResponse(stream, Error.Length == 0);
                HasResult = true;
            }
        }

        static string ReadRequestLine(NetworkStream stream)
        {
            var builder = new StringBuilder();
            byte[] one = new byte[1];

            while (builder.Length < MaximumRequestBytes)
            {
                int read = stream.Read(one, 0, 1);
                if (read <= 0)
                    break;

                char c = (char)one[0];
                if (c == '\n')
                    break;
                if (c != '\r')
                    builder.Append(c);
            }

            return builder.ToString();
        }

        static string ExtractQuery(string requestLine)
        {
            if (string.IsNullOrEmpty(requestLine))
                return string.Empty;

            int start = requestLine.IndexOf('?');
            if (start < 0)
                return string.Empty;

            int end = requestLine.IndexOf(' ', start);
            return end < 0
                ? requestLine.Substring(start + 1)
                : requestLine.Substring(start + 1, end - start - 1);
        }

        static Dictionary<string, string> ParseQuery(string query)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(query))
                return values;

            foreach (string pair in query.Split('&'))
            {
                if (pair.Length == 0)
                    continue;

                int split = pair.IndexOf('=');
                string key = split < 0 ? pair : pair.Substring(0, split);
                string value = split < 0 ? string.Empty : pair.Substring(split + 1);

                try
                {
                    values[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
                }
                catch (Exception)
                {
                    // A malformed escape sequence is not worth failing the whole redirect over.
                }
            }

            return values;
        }

        static void WriteResponse(NetworkStream stream, bool succeeded)
        {
            string title = succeeded ? "Authorization complete" : "Authorization failed";
            string detail = succeeded
                ? "You can close this tab and return to Daggerfall."
                : "Return to Daggerfall for details.";

            string html =
                "<!doctype html><html><head><meta charset=\"utf-8\"><title>DFMP</title></head>" +
                "<body style=\"font-family:sans-serif;background:#12121a;color:#e8e8f0;" +
                "display:flex;align-items:center;justify-content:center;height:100vh;margin:0\">" +
                $"<div style=\"text-align:center\"><h2>{title}</h2><p>{detail}</p></div></body></html>";

            byte[] body = Encoding.UTF8.GetBytes(html);
            byte[] header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");

            stream.Write(header, 0, header.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        void Stop()
        {
            if (listener == null)
                return;

            try
            {
                listener.Stop();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Auth] Loopback listener did not stop cleanly: {ex.Message}");
            }

            listener = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
