using System.Text;

namespace Kari.GeneratorCore.CodeAnalysis
{
    public struct CodeBuilder
    {
        public StringBuilder _stringBuilder;

        public CodeBuilder(string indentation = "")
        {
            _stringBuilder = new StringBuilder();
            CurrentIndentation = indentation;
        }

        public string CurrentIndentation { get; private set; }

        public override string ToString() => _stringBuilder.ToString();

        public void IncreaseIndent() => CurrentIndentation = CurrentIndentation + "  ";
        public void DecreaseIndent() => CurrentIndentation = CurrentIndentation.Substring(0, CurrentIndentation.Length - 2);

        public void Indent()
        {
            _stringBuilder.Append(CurrentIndentation);
        }

        public void AppendLine(string text)
        {
            _stringBuilder.Append(CurrentIndentation);
            _stringBuilder.AppendLine(text);
        }

        public void Append(string text)
        {
            _stringBuilder.Append(text);
        }

        public void StartBlock()
        {
            _stringBuilder.AppendLine(CurrentIndentation + "{");
            IncreaseIndent();
        }

        public void EndBlock()
        {
            _stringBuilder.AppendLine(CurrentIndentation + "}");
            DecreaseIndent();
        }
    }

    public struct ParameterAppender
    {
        public StringBuilder _stringBuilder;

        public ParameterAppender(int _ = 0)
        {
            _stringBuilder = new StringBuilder();
        }

        public void Append(string parameter)
        {
            _stringBuilder.Append(parameter + ", ");
        }

        public override string ToString()
        {
            if (_stringBuilder.Length == 0)
            {
                return "";
            }
            return _stringBuilder.ToString().Substring(0, _stringBuilder.Length - 2);
        }
    }
}
