using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DFMP.Runtime
{
    public enum DFMPLogRole
    {
        Server,
        Client
    }

    public static class DFMPLogRouter
    {
        const string LogsDirectoryName = "Logs";
        const string DFMPDirectoryName = "DFMP";

        static readonly object syncRoot = new object();
        static StreamWriter logWriter;
        static FileStream logStream;
        static bool initialized;
        static string logFilePath;
        static ILogHandler previousLogHandler;
        static RouterLogHandler routerLogHandler;
        static TextWriter originalOut;
        static TextWriter originalError;
        static TextWriter routedOut;
        static TextWriter routedError;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);
        const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);
        const int STD_OUTPUT_HANDLE = -11;
        const int STD_ERROR_HANDLE = -12;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool WriteConsole(IntPtr hConsoleOutput, string lpBuffer, uint nNumberOfCharsToWrite, out uint lpNumberOfCharsWritten, IntPtr lpReserved);

        static IntPtr stdOutHandle = IntPtr.Zero;
        static IntPtr stdErrHandle = IntPtr.Zero;
#endif

        public static string LogFilePath
        {
            get { return logFilePath; }
        }

        public static void Initialize(DFMPLogRole role)
        {
            lock (syncRoot)
            {
                if (initialized)
                    return;

                string logsDirectory = GetDefaultLogDirectory();
                logStream = OpenRoleLogFile(logsDirectory, role, out logFilePath);
                logWriter = new StreamWriter(logStream) { AutoFlush = true };
                WriteRawLine("=== DFMP " + role + " log started at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");

                originalOut = Console.Out;
                originalError = Console.Error;
                routedOut = new RouterTextWriter(originalOut, false);
                routedError = new RouterTextWriter(originalError, true);
                Console.SetOut(routedOut);
                Console.SetError(routedError);

                if (Application.isBatchMode)
                {
                    previousLogHandler = Debug.unityLogger.logHandler;
                    routerLogHandler = new RouterLogHandler(previousLogHandler);
                    Debug.unityLogger.logHandler = routerLogHandler;
                }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                try
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                    stdOutHandle = GetStdHandle(STD_OUTPUT_HANDLE);
                    stdErrHandle = GetStdHandle(STD_ERROR_HANDLE);
                }
                catch
                {
                }
#endif

                Application.logMessageReceivedThreaded += OnLogMessageReceived;
                initialized = true;
            }
        }

        public static string GetDefaultLogDirectory()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), LogsDirectoryName, DFMPDirectoryName);
        }

        public static string GetLogFileName(DFMPLogRole role, int index)
        {
            string prefix = role == DFMPLogRole.Server ? "server" : "client";
            return index == 0 ? prefix + ".log" : prefix + index + ".log";
        }

        public static string GetPreviousLogFileName(DFMPLogRole role, int index)
        {
            string prefix = role == DFMPLogRole.Server ? "server" : "client";
            return index == 0 ? prefix + "-prev.log" : prefix + index + "-prev.log";
        }

        public static FileStream OpenRoleLogFile(string logsDirectory, DFMPLogRole role, out string path)
        {
            if (string.IsNullOrEmpty(logsDirectory))
                throw new ArgumentException("Log directory must not be empty.", "logsDirectory");

            Directory.CreateDirectory(logsDirectory);

            for (int index = 0; index < 1000; index++)
            {
                path = Path.Combine(logsDirectory, GetLogFileName(role, index));
                string previousPath = Path.Combine(logsDirectory, GetPreviousLogFileName(role, index));

                try
                {
                    RotateExistingLogFile(path, previousPath);
                    return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                }
                catch (IOException)
                {
                }
            }

            path = Path.Combine(logsDirectory, GetLogFileName(role, 1000));
            string fallbackPreviousPath = Path.Combine(logsDirectory, GetPreviousLogFileName(role, 1000));
            RotateExistingLogFile(path, fallbackPreviousPath);
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }

        static void RotateExistingLogFile(string currentPath, string previousPath)
        {
            if (!File.Exists(currentPath))
                return;

            if (File.Exists(previousPath))
                File.Delete(previousPath);

            File.Move(currentPath, previousPath);
        }

        static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            string message = FormatMessage(condition, stackTrace, type);
            WriteRaw(message);
            WriteToConsole(message, type);
        }

        static void WriteUnityLog(string condition, string stackTrace, LogType type)
        {
            string message = FormatMessage(condition, stackTrace, type);
            WriteRaw(message);
            WriteToConsole(message, type);
        }

        static void WriteConsoleLine(string message, bool isError)
        {
            if (string.IsNullOrEmpty(message))
                return;

            string prefix = isError ? "[Console Error] " : "[Console] ";
            WriteRawLine(prefix + message);
        }

        static void WriteRawLine(string message)
        {
            WriteRaw(message + Environment.NewLine);
        }

        static void WriteRaw(string message)
        {
            lock (syncRoot)
            {
                if (logWriter != null)
                    logWriter.Write(message);
            }
        }

        static string FormatMessage(string condition, string stackTrace, LogType type)
        {
            string prefix = string.Empty;
            if (type == LogType.Error || type == LogType.Exception)
                prefix = "[" + type + "] ";
            else if (type == LogType.Warning)
                prefix = "[Warning] ";

            string message = prefix + condition + Environment.NewLine;
            if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stackTrace))
                message += stackTrace + Environment.NewLine;

            return message;
        }

        static void WriteToConsole(string message, LogType type)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            IntPtr targetHandle = (type == LogType.Error || type == LogType.Exception) ? stdErrHandle : stdOutHandle;
            if (targetHandle == IntPtr.Zero || targetHandle == new IntPtr(-1))
                targetHandle = stdOutHandle;

            if (targetHandle != IntPtr.Zero && targetHandle != new IntPtr(-1))
            {
                if (WriteConsole(targetHandle, message, (uint)message.Length, out uint written, IntPtr.Zero))
                    return;
            }
#endif
            try
            {
                if (type == LogType.Error || type == LogType.Exception)
                {
                    originalError.Write(message);
                    originalError.Flush();
                }
                else
                {
                    originalOut.Write(message);
                    originalOut.Flush();
                }
            }
            catch
            {
            }
        }

        public static void Shutdown()
        {
            lock (syncRoot)
            {
                if (!initialized)
                    return;

                Application.logMessageReceivedThreaded -= OnLogMessageReceived;
                initialized = false;

                if (routerLogHandler != null && Debug.unityLogger.logHandler == routerLogHandler)
                    Debug.unityLogger.logHandler = previousLogHandler;

                previousLogHandler = null;
                routerLogHandler = null;

                if (originalOut != null)
                    Console.SetOut(originalOut);

                if (originalError != null)
                    Console.SetError(originalError);

                routedOut = null;
                routedError = null;
                originalOut = null;
                originalError = null;

                if (logWriter != null)
                {
                    logWriter.Dispose();
                    logWriter = null;
                }

                if (logStream != null)
                {
                    logStream.Dispose();
                    logStream = null;
                }
            }
        }

        class RouterLogHandler : ILogHandler
        {
            readonly ILogHandler innerHandler;

            public RouterLogHandler(ILogHandler innerHandler)
            {
                this.innerHandler = innerHandler;
            }

            public void LogException(Exception exception, UnityEngine.Object context)
            {
                if (exception != null)
                    WriteUnityLog(exception.ToString(), null, LogType.Exception);

                if (innerHandler != null)
                    innerHandler.LogException(exception, context);
            }

            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            {
                string condition;
                try
                {
                    condition = string.Format(format, args);
                }
                catch
                {
                    condition = format;
                }

                WriteUnityLog(condition, null, logType);

                if (innerHandler != null)
                    innerHandler.LogFormat(logType, context, format, args);
            }
        }

        class RouterTextWriter : TextWriter
        {
            readonly TextWriter innerWriter;
            readonly bool isError;

            public RouterTextWriter(TextWriter innerWriter, bool isError)
            {
                this.innerWriter = innerWriter;
                this.isError = isError;
            }

            public override System.Text.Encoding Encoding
            {
                get { return innerWriter.Encoding; }
            }

            public override void Write(char value)
            {
                innerWriter.Write(value);
            }

            public override void Write(string value)
            {
                innerWriter.Write(value);

                if (!string.IsNullOrEmpty(value))
                    WriteConsoleLine(value.TrimEnd('\r', '\n'), isError);
            }

            public override void WriteLine(string value)
            {
                innerWriter.WriteLine(value);
                WriteConsoleLine(value, isError);
            }

            public override void Flush()
            {
                innerWriter.Flush();
            }
        }
    }
}