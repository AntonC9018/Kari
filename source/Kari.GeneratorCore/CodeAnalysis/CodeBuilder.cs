using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Kari.GeneratorCore.CodeAnalysis
{
    public struct CodeBuilder
    {
        public StringBuilder _stringBuilder;

        public CodeBuilder(string indentation, string initialIndentation)
        {
            _stringBuilder = new StringBuilder();
            Indentation = indentation;
            CurrentIndentation = initialIndentation;
        }

        public CodeBuilder NewWithIndentation()
        {
            return new CodeBuilder(Indentation, CurrentIndentation);
        }

        public string CurrentIndentation { get; private set; }
        public string Indentation { get; }

        public override string ToString() => _stringBuilder.ToString();

        public void IncreaseIndent() => CurrentIndentation = CurrentIndentation + "    ";
        public void DecreaseIndent() => CurrentIndentation = CurrentIndentation.Substring(0, CurrentIndentation.Length - Indentation.Length);

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
            DecreaseIndent();
            _stringBuilder.AppendLine(CurrentIndentation + "}");
        }
    }

    public struct ListBuilder
    {
        public StringBuilder _stringBuilder;
        public string _separator;

        public ListBuilder(string separator)
        {
            _stringBuilder = new StringBuilder();
            _separator = separator;
        }

        public void Append(string parameter)
        {
            _stringBuilder.Append(parameter + _separator);
        }

        public override string ToString()
        {
            if (_stringBuilder.Length == 0)
            {
                return "";
            }
            return _stringBuilder.ToString(0, _stringBuilder.Length - _separator.Length);
        }
    }

    public struct EvenTableBuilder
    {
        private string[] _title;
        private List<string>[] _columns;

        public int Width => _columns.Length;
        public int Height => _columns[0].Count;

        public EvenTableBuilder(int numCols)
        {
            Debug.Assert(numCols > 0, "Cannot create a 0-wide table");
            _columns = new List<string>[numCols];
            for (int i = 0; i < numCols; i++) _columns[i] = new List<string>();
            _title = null;
        }

        public EvenTableBuilder(params string[] title)
        {
            Debug.Assert(title.Length > 0, "Cannot create a 0-wide table");
            _columns = new List<string>[title.Length];
            for (int i = 0; i < title.Length; i++) _columns[i] = new List<string>();
            _title = title;
        }

        public void Append(int column, string text)
        {
            Debug.Assert(column < Width, $"Column {column} is wider than the table");
            _columns[column].Add(text);
        }

        public void SetTitle(params string[] title)
        {
            Debug.Assert(title.Length == Width);
            _title = title;
        }

        public string ToString(string spacing = "    ")
        {
            var builder = new StringBuilder();
            var maxLengths = new int[Width];

            int Max(int a, int b) => a > b ? a : b;
            
            for (int col = 0; col < _columns.Length; col++)
            {
                for (int row = 0; row < _columns[col].Count; row++)
                {
                    maxLengths[col] = Max(maxLengths[col], _columns[col][row].Length);
                }
            }

            if (_title != null)
            {
                for (int col = 0; col < Width - 1; col++)
                {
                    maxLengths[col] = Max(maxLengths[col], _title[col].Length);
                    builder.Append(_title[col]);
                    builder.Append(' ', maxLengths[col] - _title[col].Length);
                    builder.Append(spacing);
                }
                
                maxLengths[Width - 1] = Max(maxLengths[Width - 1], _title[Width - 1].Length);
                builder.Append(_title[Width - 1]);
                builder.AppendLine();

                for (int col = 0; col < Width; col++)
                {
                    builder.Append('-', maxLengths[col]);
                }
                builder.Append('-',  spacing.Length * (Width - 1));
                builder.AppendLine();
            }

            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width - 1; col++)
                {
                    var column = _columns[col];
                    var str = row < column.Count ? column[row] : "";
                    builder.Append(str);
                    builder.Append(' ', maxLengths[col] - str.Length);
                    builder.Append(spacing); 
                }

                var lastColumn = _columns[Width - 1];
                if (row < lastColumn.Count)
                {
                    builder.Append(lastColumn[row]);
                }
                builder.AppendLine();
            }

            return builder.ToString();
        }
    }
}
