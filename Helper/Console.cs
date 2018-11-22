using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Helper
{
    /// <summary>
    /// 线程安全的
    /// </summary>
    public static class Console
    {
        private static int _idMainThread;
        private static Action<string> _lpfnLog = _Log;
        private static Action<string> _lpfnLogError = _LogError;
        private static Action<string> _lpfnLogWarning = _LogWarning;

        static Console()
        {
            _idMainThread = Thread.CurrentThread.ManagedThreadId;
        }

        private static string _AppendStackTrace(StringBuilder sbText)
        {
            StackTrace trace = new StackTrace(2, true);
            StackFrame[] stackFrames = trace.GetFrames();
            if (stackFrames != null)
                foreach (StackFrame frame in stackFrames)
                {
                    sbText.AppendFormat("{0} (at {1}:{2})\n", frame.GetMethod(), frame.GetFileName(), frame.GetFileLineNumber());
                }
            return sbText.ToString();
        }

        private static string _FormatMessage(string format, params object[] args)
        {
            return format != null ? string.Format(null, format, args) : "null format (-__-)";
        }

        private static void _Log(string message)
        {
            Core.DebugLog.Log(message);
        }

        private static void _LogError(string message)
        {
            Core.DebugLog.LogError(message);
        }

        private static void _LogWarning(string message)
        {
            Core.DebugLog.LogWarning(message);
        }

        private static void _WriteLine(Action<string> output, string message)
        {
            bool isMainThread = Thread.CurrentThread.ManagedThreadId == _idMainThread;
            try
            {
                if (isMainThread || _idMainThread == 0)
                {
                    output(message);
                }
                else if (OS.IsEditor)
                {
                    StringBuilder sbText = new StringBuilder(message);
                    sbText.AppendLine();
                    string text = _AppendStackTrace(sbText);
                    Loom.QueueOnMainThread(()=> {
                        output(text);
                    });
                }
                else
                {
                    Loom.QueueOnMainThread(() =>
                    {
                        output(message);
                    });
                }
            }
            catch (MissingMethodException)
            {
                System.Console.WriteLine(message);
            }
        }

        public static void Log(string message)
        {
            _WriteLine(_lpfnLog, message);
        }

        public static void Log(string format, params object[] args)
        {
            _WriteLine(_lpfnLog, _FormatMessage(format, args));
        }
        public static void Error(string message)
        {
            _WriteLine(_lpfnLogError, message);
        }

        public static void Error(string format, params object[] args)
        {
            _WriteLine(_lpfnLogError, _FormatMessage(format, args));
        }
        public static void Warning(string message)
        {
            _WriteLine(_lpfnLogWarning, message);
        }

        public static void Warning(string format, params object[] args)
        {
            _WriteLine(_lpfnLogWarning, _FormatMessage(format, args));
        }
    }

}


