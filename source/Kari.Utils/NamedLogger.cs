using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Spectre.Console;

namespace Kari.Utils
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
    public readonly struct NamedLogger
    {
        private static object _MessageLock = new object();
        private static bool _HasErrors;

        /// <summary>
        /// Whether a message with the type of error has been reported so far.
        /// </summary>
        public static bool AnyLoggerHasErrors => _HasErrors;
        public bool AnyHasErrors => AnyLoggerHasErrors;
        public string Name { get; }

        public NamedLogger(string name)
        {
            Name = name;
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

        public void Log(string message)
        {
            Log(message, LogType.Message);
        }

        public void Log(string message, LogType type)
        {
            switch (type)
            {
                case LogType.Message:     AnsiConsole.Foreground = Color.Grey;      break;
                case LogType.Error:       AnsiConsole.Foreground = Color.Red;       break;
                case LogType.Information: AnsiConsole.Foreground = Color.Cyan1;     break;
                case LogType.Debug:       AnsiConsole.Foreground = Color.DarkRed;   break;
                case LogType.Warning:     AnsiConsole.Foreground = Color.Yellow;    break;
            }
            AnsiConsole.WriteLine(String.Concat("[", Name, "]: ", message));
            _HasErrors = _HasErrors || (type == LogType.Error);
        }

        public static void LogPlain(string message)
        {
            Console.ForegroundColor = (ConsoleColor) LogType.Message;
            Console.WriteLine(message);
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
    

    public struct Measurer
    {
        private string _operationName;
        private long _startTime;
        private NamedLogger _logger;

        public Measurer(NamedLogger logger)
        { 
            _startTime = -1;
            _logger = logger;
            _operationName = "";
        }

        public void Start(string operationName)
        {
            _startTime = Stopwatch.GetTimestamp();
            _operationName = operationName;

            _logger.Log(operationName + " Started.", LogType.Information); 
        }

        public void Stop()
        {
            var elapsed = new TimeSpan(Stopwatch.GetTimestamp() - _startTime);
            _logger.Log(_operationName + " Completed. Time elapsed: " + elapsed.ToString(), LogType.Information);
        }
    }
}