using System;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Helper;
using UnityEngine;

namespace Download
{
    [StructLayout(LayoutKind.Sequential)]
    public struct HttpClientArgument
    {
        public string Url;
        public int Timeout;
        public object Param1;
        public byte[] PostData;
        public Func<byte[], object, byte[]> ThreadDecompressor;

        public void SetPostData(byte[] data)
        {
            if(data == null || data.Length <= 0) return;
            PostData = new byte[data.Length];
            Array.Copy(data, 0, PostData, 0, data.Length);
        }

        public override string ToString()
        {
            return string.Format("[url={0}, timeout={1}, param1={2}]", Url, Timeout, Param1 ?? "(null)");
        }
    }
    public class HttpClient : IDisposable
    {
        private Action<HttpClient> _handler;
        private bool _isDisposed;
        private const int _kDefaultTimeout = 20000;
        private const int _kMaxRetryNumber = 1;
        private static readonly byte[] _zeroLengthBytes = new byte[0];

        public HttpClientArgument Argument { get; private set; }

        public byte[] Bytes { get; private set; }

        public Exception Error { get; private set; }

        public bool IsDone { get; private set; }

        public float Progress { get; private set; }

        public long TotalLength { get; private set; }

        public string Url { get; private set; }
        private HttpClient(HttpClientArgument argument, Action<HttpClient> handler)
        {
            Url = argument.Url;
            if (argument.Timeout <= 0)
            {
                argument.Timeout = _kDefaultTimeout;
            }
            Argument = argument;
            Bytes = _zeroLengthBytes;
            _handler = handler;
            ThreadPool.QueueUserWorkItem(_lpfnThreadDownload);
        }

        private byte[] _CheckDecompress(byte[] rawBuffer)
        {
            byte[] buffer = rawBuffer;
            if (Argument.ThreadDecompressor != null)
            {
                try
                {
                    buffer = Argument.ThreadDecompressor(rawBuffer, Argument.Param1);
                }
                catch (Exception exception)
                {
                    Error = Error ?? exception;
                    Helper.Console.Warning("[HttpClient._CheckDecompress()] url={0}, rawBuffer.Length={1}, ex={2}", Url, rawBuffer.Length, exception);
                }
            }
            return buffer;
        }

        private void _CheckSpeedTooSlow(int receivedSize, float startTime)
        {
            float psTime = OS.time - startTime;
            if (psTime > 2f)
            {
                float pb =  receivedSize / psTime;
                if (pb < 1024)
                {
                    Helper.Console.Warning("_CheckSpeedTooSlow speed : " + pb);
                    throw new IOException("download speed is too slow=" + pb.ToString("F2"));
                }
            }
        }

        private string _GetUserAgent()
        {
#if UNITY_ANDROID
            return "Mozilla/5.0 (Linux; Android 5.0; SM-G900P Build/LRX21T) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/48.0.2564.23 Mobile Safari/537.36";
#elif UNITY_IPHONE
            return "Mozilla/5.0 (iPhone; CPU iPhone OS 9_1 like Mac OS X) AppleWebKit/601.1.46 (KHTML, like Gecko) Version/9.0 Mobile/13B143 Safari/601.1";
#else
            return "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/53.0.2785.116 Safari/537.36";
#endif
        }

        private void _lpfnThreadDownload(object state)
        {
            byte[] rawBuffer = null;
            int num = 0;
            while (true)
            {
                if (_WebRequestRawBuffer(ref rawBuffer))
                {
                    Bytes = _CheckDecompress(rawBuffer);
                    _OnExitDownload();
                    return;
                }
                if (++num > _kMaxRetryNumber)
                {
                    break;
                }
                Helper.Console.Log("[HttpClient._lpfnThreadDownload()] Retry download url={0}, retryNumber={1}, _isDisposed={2}", Url, num, _isDisposed);
            }
            this._RetryTooManyTimes(rawBuffer);
            this._OnExitDownload();
        }

        private void _OnExitDownload()
        {
           Argument = new HttpClientArgument
            {
                Url = Argument.Url,
                Timeout = Argument.Timeout,
                Param1 = Argument.Param1,
                ThreadDecompressor = null,
                PostData = null,
            };
           IsDone = true;
            Progress = 1f;
            if (_handler != null)
            {
                Loom.QueueOnMainThread(()=> {
                    CallbackTools.Handle(ref _handler, this, "[HttpClient:_OnExitDownload()]");
                });
            }
        }

        private void _RetryTooManyTimes(byte[] rawBuffer)
        {
            Bytes = _zeroLengthBytes;
            string message = string.Format("[HttpClient._OnRetryTooManyTimes()] Retry too many times. url={0}]", Url);
            Error = new Exception(message);
            Helper.Console.Warning(message);
        }

        private bool _WebRequestRawBuffer(ref byte[] rawBuffer)
        {
            int contentLength = 0;
            int offset = 0;
            HttpWebResponse response = null;
            HttpWebRequest request = null;
            try
            {
                request = WebRequest.Create(Url) as HttpWebRequest;
                if (request == null) return false;
                string str = this._GetUserAgent();
                if (str != null)
                {
                    request.UserAgent = str;
                }
                request.Timeout = Argument.Timeout;
                request.ReadWriteTimeout = 5000;
                request.Proxy = null;
                if (Argument.PostData != null)
                {
                    request.Method = "POST";
                    request.ContentType = "application/x-www-form-urlencoded";
                    request.ContentLength = Argument.PostData.Length;
                    using (Stream requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(Argument.PostData, 0, Argument.PostData.Length);
                        requestStream.Flush();
                        requestStream.Close();
                    }
                }
                else
                {
                    request.Method = "GET";
                }
                response = (HttpWebResponse) request.GetResponse();
                using (Stream responseStream = response.GetResponseStream())
                {
                    TotalLength = response.ContentLength;
                    contentLength = (int)response.ContentLength;
                    if ((rawBuffer == null) || (rawBuffer.Length != contentLength))
                    {
                        rawBuffer = new byte[contentLength];
                    }
                    float time = OS.time;
                    while (!_isDisposed)
                    {
                        offset += responseStream.Read(rawBuffer, offset, contentLength - offset);
                        if (contentLength > 0)
                        {
                            Progress = (float)offset / (float)contentLength;
                        }
                        if (offset == contentLength)
                        {
                            response.Close();
                            response = null;
                            request.Abort();
                            request = null;
                            return true;
                        }
                        _CheckSpeedTooSlow(offset, time);
                    }
                }
            }
            catch (Exception exception)
            {
                if (response != null)
                {
                    response.Close();
                    response = null;
                }
                if (request != null)
                {
                    request.Abort();
                    request = null;
                }
                Helper.Console.Warning( "[HttpClient._WebRequestRawBuffer()] url={0}, timeout={1}, contentLength={2}, receivedSize={3}, ex={4}",
                    Url, Argument.Timeout, contentLength, offset, exception);
                Error = Error ?? exception;
            }
            return false;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                Bytes = _zeroLengthBytes;
                Argument = new HttpClientArgument();
                _handler = null;
                _isDisposed = true;
                Error = null;
            }
        }

        public override string ToString()
        {
            return string.Format("[HttpClient: url={0}, isDone={1}, progress={2}, error={3}]", Url, IsDone, Progress, Error);
        }

        public static HttpClient TryDownload(HttpClientArgument argument, Action<HttpClient> handler)
        {
            return new HttpClient(argument, handler);
        }

        public static bool IsExist(string url)
        {
            HttpWebRequest request = null;
            HttpWebResponse response = null;
            try
            {
                request = WebRequest.Create(url) as HttpWebRequest;
                if (request == null) return false;
                request.Method = "HEAD";
                request.Timeout = _kDefaultTimeout;
                response = (HttpWebResponse)request.GetResponse();
                return (response.StatusCode == HttpStatusCode.OK);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (response != null)
                {
                    response.Close();
                    response = null;
                }
                if (request != null)
                {
                    request.Abort();
                    request = null;
                }
            }
        }
        public static long GetFileContentLength(string url)
        {
            HttpWebRequest request = null;
            HttpWebResponse response = null;
            try
            {
                request = WebRequest.Create(url) as HttpWebRequest;
                if (request == null) return -1;
                request.Method = "GET";
                request.Timeout = _kDefaultTimeout;
                response = (HttpWebResponse)request.GetResponse();
                if (HttpStatusCode.OK != response.StatusCode)
                {
                    return -1;
                }
                return response.ContentLength;
            }
            catch (Exception)
            {
                return -1;
            }
            finally
            {
                if (response != null)
                {
                    response.Close();
                    response = null;
                }
                if (request != null)
                {
                    request.Abort();
                    request = null;
                }
            }
        }
    }
}

