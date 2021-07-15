
namespace Kari
{
    using System.Text;
    public struct Parser
    {
        private int _currentIndex;
        public string Source { get; }

        public Parser(string source)
        {
            Source = source;
            _currentIndex = 0;
        }

        public bool IsEmpty => _currentIndex >= Source.Length;
        public char Current => Source[_currentIndex];

        public void SkipWhitespace()
        {
            while (!IsEmpty && char.IsWhiteSpace(Current))
            {
                _currentIndex++;
            }
        }

        public string GetCommandName()
        {
            if (IsEmpty) return null;

            int commandNameStart = _currentIndex;
            while (!IsEmpty && !char.IsWhiteSpace(Current))
            {
                _currentIndex++;
            }
            if (_currentIndex == commandNameStart)
            {
                _currentIndex = commandNameStart;
                return null;
            }
            return Source.Substring(commandNameStart, _currentIndex - commandNameStart);
        }

        public string GetName()
        {
            if (IsEmpty) return null;

            int nameStart = _currentIndex;
            while (!IsEmpty && !char.IsWhiteSpace(Current))
            {
                _currentIndex++;
            }
            return Source.Substring(nameStart, _currentIndex - nameStart);
        }

        /// Assume no escapes and double quotes
        public string GetString()
        {
            if (IsEmpty) return null;
            // starting with the option symbol is invalid
            if (Current == '-') return null;
            // "string"
            if (Current == '"')
            {
                int quoteIndex = _currentIndex;
                _currentIndex++;
                while (!IsEmpty && Current != '"')
                {
                    _currentIndex++;
                }
                // "string
                if (IsEmpty) 
                {
                    _currentIndex = quoteIndex;
                    return null;
                }
                _currentIndex++;
                int start  = quoteIndex + 1;
                int length = _currentIndex - start - 1;
                return Source.Substring(start, length);
            }
            return GetName();
        }

        public bool TryGetOption(out Option option)
        {
            option = default;
            if (IsEmpty) return false;
            if (Current != '-') return false;

            int start = _currentIndex;
            // option (the identifier part)
            while (!IsEmpty && !char.IsWhiteSpace(Current) && Current != '=')
            {
                _currentIndex++;
            }

            var nameStart = start + 1;
            option.Name = Source.Substring(nameStart, _currentIndex - nameStart);

            SkipWhitespace();

            // if no =, parse as a flag 
            if (IsEmpty || Current != '=') 
            {
                return true;
            }

            // =
            _currentIndex++;
            int valueStart = _currentIndex;

            option.Value = GetString();
            if (option.Value is null)
            {
                _currentIndex = start;
                return false;
            }

            return true;
        }
    }

    public struct Option
    {
        public string Name;
        public string Value;

        public bool GetFlagValue(bool defaultValue = true) 
        {
            if (Value == null) return defaultValue;
            return bool.Parse(Value);
        }
    }

    public class CommandContext
    {
        public StringBuilder _builder;
        public Parser Parser;

        public CommandContext(string rawArguments)
        {
            _builder = new StringBuilder();
            Parser = new Parser(rawArguments);
        }

        public void Log(string message)
        {
            _builder.Append(message);
        }
    }

    public interface ICommand
    {
        string Execute(CommandContext context);
        string HelpMessage { get; }
    }

    public static class ExecuteHelper
    {
        public static bool IsHelp(Parser parser)
        {
            Option currentOption;

            return parser.TryGetOption(out currentOption) 
                && currentOption.Name == "help" 
                && currentOption.GetFlagValue();
        }
    }
}