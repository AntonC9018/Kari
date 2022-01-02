using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

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
        public static readonly Logger Debug = new Logger("DEBUG", LogType.Debug);

        public Logger(string name, LogType defaultLogType = LogType.Message)
        {
            _defaultLogType = defaultLogType;
            _name = name;
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
            Log(message, _defaultLogType);
        }

        public void Log(string message, LogType type)
        {
            LogNoLock(message, type);
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

    // public struct Measurer : IDisposable
    // {
    //     private string _operationName;
    //     private Logger _logger;
    //     private long _startTime;

    //     /// <summary>
    //     /// Use like this: using (Measurer.Start("operation")) { stuff; }
    //     /// </summary>
    //     public static Measurer Start(Logger logger, string operationName)
    //     {
    //         Measurer result;
    //         result._logger = logger;
    //         result._operationName = operationName;
    //         result._startTime = Stopwatch.GetTimestamp();
            
    //         logger.Log(operationName + " Started.", LogType.Information);
            
    //         return result;
    //     }

    //     public void Dispose()
    //     {
    //         var elapsed = new TimeSpan(Stopwatch.GetTimestamp() - _startTime);
    //         _logger.Log(_operationName + " Ended. Elapsed time: " + elapsed.ToString(), LogType.Information);
    //     }

    //     public void Stop() { Dispose(); }
    // }

    public struct Measurer
    {
        private string _operationName;
        private long _startTime;
        private Logger _logger;

        public Measurer(Logger logger)
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