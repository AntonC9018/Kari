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
        Warning = ConsoleColor.Yellow,
        Information = ConsoleColor.Cyan
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
        public static bool HasErrors => _HasErrors;
        private readonly string _name;

        public Logger(string name)
        {
            _name = name;
        }

        /// <summary>
        /// Measures an operation.
        /// </summary>
        public async Task Measure(string operationName, Task task)
        {
            Log(operationName + " Started.", LogType.Information);
            var s = Stopwatch.StartNew();
            await task;
            Log(operationName + " Completed. Time elapsed: " + s.ToString(), LogType.Information);
        }

        /// <summary>
        /// Measures an operation.
        /// </summary>
        public void MeasureSync(string operationName, System.Action operation)
        {
            Log(operationName + " Started.", LogType.Information);
            var s = Stopwatch.StartNew();
            operation();
            Log(operationName + " Completed. Time elapsed: " + s.ToString(), LogType.Information);
        }

        /// <summary>
        /// Measures an operation.
        /// </summary>
        public async Task Measure(string operationName, System.Action operation)
        {
            Log(operationName + " Started.", LogType.Information);
            var s = Stopwatch.StartNew();
            await Task.Run(operation);
            Log(operationName + " Completed. Time elapsed: " + s.ToString(), LogType.Information);
        }

        /// <summary>
        /// Measures an operation.
        /// </summary>
        public void MeasureNoLock(string operationName, System.Action operation)
        {
            LogNoLock(operationName + " Started.", LogType.Information);
            var s = Stopwatch.StartNew();
            operation();
            LogNoLock(operationName + " Completed. Time elapsed: " + s.ToString(), LogType.Information);
        }

        public void Log(string message, LogType type = LogType.Message)
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
            Console.WriteLine($"[{_name}]:\t{message}");
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
    }
}