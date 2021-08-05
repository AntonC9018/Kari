using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Kari.GeneratorCore.Workflow
{
    public enum LogType
    {
        Message = ConsoleColor.Gray,
        Error = ConsoleColor.Red,
        Warning = ConsoleColor.DarkYellow,
        Information = ConsoleColor.Cyan,
        Debug = ConsoleColor.DarkRed
    }

    /// <summary>
    /// A helper struct that colors output to the stdout.
    /// The logging is thread-safe, but all threads would still dump their data to a single output stream.
    /// </summary>
    public readonly struct Logger
    {
        private static object _MessageLock = new object();
        private static bool _HasErrors;

        /// <summary>
        /// Whether a message with the type of error has been reported so far.
        /// </summary>
        public static bool AnyLoggerHasErrors => _HasErrors;
        private readonly string _name;
        private readonly LogType _defaultLogType;
        public static readonly Logger Debug = new Logger("DEBUG");

        public Logger(string name, LogType defaultLogType = LogType.Message)
        {
            _defaultLogType = defaultLogType;
            _name = name;
        }

        /// <summary>
        /// Measures an operation.
        /// </summary>
        public async Task MeasureAsync(string operationName, Task task)
        {
            Log(operationName + " Started.", LogType.Information);
            var s = Stopwatch.StartNew();
            await task;
            Log(operationName + " Completed. Time elapsed: " + s.Elapsed.ToString(), LogType.Information);
        }

        /// <summary>
        /// Measures an operation.
        /// </summary>
        public void MeasureSync(string operationName, System.Action operation)
        {
            Log(operationName + " Started.", LogType.Information);
            var s = Stopwatch.StartNew();
            operation();
            Log(operationName + " Completed. Time elapsed: " + s.Elapsed.ToString(), LogType.Information);
        }

        /// <summary>
        /// Measures an operation.
        /// </summary>
        public async Task MeasureAsync(string operationName, System.Action operation)
        {
            Log(operationName + " Started.", LogType.Information);
            var s = Stopwatch.StartNew();
            await Task.Run(operation);
            Log(operationName + " Completed. Time elapsed: " + s.Elapsed.ToString(), LogType.Information);
        }

        /// <summary>
        /// Measures an operation.
        /// </summary>
        public void MeasureNoLock(string operationName, System.Action operation)
        {
            LogNoLock(operationName + " Started.", LogType.Information);
            var s = Stopwatch.StartNew();
            operation();
            LogNoLock(operationName + " Completed. Time elapsed: " + s.Elapsed.ToString(), LogType.Information);
        }

        public void Log(string message)
        {
            Log(message, _defaultLogType);
        }

        public void Log(string message, LogType type)
        {
            lock (_MessageLock)
            {
                LogNoLock(message, type);
            }
        }

        public void LogErrorNoLock(string message)
        {
            LogNoLock(message, LogType.Error);
        }


        public void LogNoLock(string message, LogType type = LogType.Message)
        {
            Console.ForegroundColor = (ConsoleColor) type;
            Console.WriteLine($"[{_name}]: {message}");
            _HasErrors = _HasErrors || (type == LogType.Error);
        }

        public void LogError(string message)
        {
            Log(message, LogType.Error);
        }

        public void LogWarning(string message)
        {
            Log(message, LogType.Warning);
        }

        public void LogInfo(string message)
        {
            Log(message, LogType.Information);
        }

        public void LogDebug(string message)
        {
            Log(message, LogType.Debug);
        }
    }
}