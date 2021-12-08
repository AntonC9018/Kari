using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using static System.Diagnostics.Debug;

namespace Kari.Utils
{
    public interface IAppend
    {
        void Append(string text);
    }
    public interface IIndent
    {
        void Indent();
    }

    /// <summary>
    /// A utility string builder with indentation support.
    /// </summary>
    public struct CodeBuilder : IAppend //, IIndent
    {
        public static CodeBuilder Create() { return new CodeBuilder("    ", ""); }
        
        private readonly StringBuilder _stringBuilder;
        public string CurrentIndentation;

        /// <summary>
        /// The string used for indentation.
        /// One step in indentation amounts to a copy of this string being added to the indentation.
        /// </summary>
        public string Indentation { get; }

        /// <summary>
        /// A utility string builder with indentation support.
        /// </summary>
        public CodeBuilder(string indentation, string initialIndentation = "")
        {
            _stringBuilder = new StringBuilder();
            CurrentIndentation = initialIndentation;
            Indentation = indentation;
        }

        /// <summary>
        /// Returns an empty CodeBuilder with the same indentation settings
        /// and the same current indentation.
        /// </summary>
        public CodeBuilder NewWithPreservedIndentation()
        {
            return new CodeBuilder(Indentation, CurrentIndentation);
        }

        public override string ToString() => _stringBuilder.ToString();
        public void IncreaseIndent() => CurrentIndentation = CurrentIndentation + "    ";
        public void DecreaseIndent() => CurrentIndentation = CurrentIndentation.Substring(0, CurrentIndentation.Length - Indentation.Length);

        /// <summary>
        /// Appends the indentation string to the output.
        /// </summary>
        public void Indent()
        {
            _stringBuilder.Append(CurrentIndentation);
        }

        /// <summary>
        /// Appends the indentation, the text and a new line character to the output.
        /// </summary>
        public void AppendLine(string text = "")
        {
            _stringBuilder.Append(CurrentIndentation);
            _stringBuilder.AppendLine(text);
        }

        public void Append(string source, int startIndex, int count)
        {
            _stringBuilder.Append(source, startIndex, count);
        }

        /// <summary>
        /// Appends only the text to the output.
        /// </summary>
        public void Append(string text)
        {
            _stringBuilder.Append(text);
        }

        /// <summary>
        /// Appends only the text to the output.
        /// </summary>
        public void NewLine()
        {
            _stringBuilder.AppendLine();
        }

        /// <summary>
        /// Appends '{' and increases indentation.
        /// </summary>
        public void StartBlock()
        {
            _stringBuilder.AppendLine(CurrentIndentation + "{");
            IncreaseIndent();
        }

        /// <summary>
        /// Appends '}' and decreases indentation.
        /// </summary>
        public void EndBlock()
        {
            DecreaseIndent();
            _stringBuilder.AppendLine(CurrentIndentation + "}");
        }

        public void Clear()
        {
            CurrentIndentation = "";
            _stringBuilder.Clear();
        }
    }

    /// <summary>
    /// A helper for building text lists, e.g. the parameters of a function call like "a, b, c".
    /// </summary>
    public readonly struct ListBuilder
    {
        private readonly StringBuilder _stringBuilder;
        private readonly string _separator;

        /// <summary>
        /// Creates a list builder.
        /// The separator indicates the string used to concatenate the added elements.
        /// For e.g. a list of parameters, one may use ", " as the separator, in to get e.g. "a, b, c".
        /// </summary>
        public ListBuilder(string separator)
        {
            _stringBuilder = new StringBuilder();
            _separator = separator;
        }

        /// <summary>
        /// Adds the given element to the result, concatenating it using the separator.
        /// </summary>
        public void Append(string element)
        {
            _stringBuilder.Append(element);
            _stringBuilder.Append(_separator);
        }

        public override string ToString()
        {
            if (_stringBuilder.Length == 0)
            {
                return "";
            }
            return _stringBuilder.ToString(0, _stringBuilder.Length - _separator.Length);
        }

        public void Clear()
        {
            _stringBuilder.Clear();
        }
    }

    /// <summary>
    /// A helper for building nicely formatted tables in text.
    /// </summary>
    public struct EvenTableBuilder
    {
        private string[] _title;
        private readonly List<string>[] _columns;

        /// <summary>
        /// The number of columns.
        /// </summary>
        public int Width => _columns.Length;

        /// <summary>
        /// The number of rows.
        /// </summary>
        public int Height => _columns[0].Count;

        /// <summary>
        /// Creates a table without a title.
        /// </summary>
        public EvenTableBuilder(int numCols)
        {
            Debug.Assert(numCols > 0, "Cannot create a 0-wide table");
            _columns = new List<string>[numCols];
            for (int i = 0; i < numCols; i++) _columns[i] = new List<string>();
            _title = null;
        }

        /// <summary>
        /// Creates a table builder with the given title.
        /// The number of columns will be the same as the number of elements in the title.
        /// Every element of the title array applies to the column at that position.
        /// </summary>
        public EvenTableBuilder(params string[] title)
        {
            Debug.Assert(title.Length > 0, "Cannot create a 0-wide table");
            _columns = new List<string>[title.Length];
            for (int i = 0; i < title.Length; i++) _columns[i] = new List<string>();
            _title = title;
        }

        /// <summary>
        /// Appends the given text to a new row in the given column.
        /// </summary>
        public void Append(int column, string text)
        {
            Debug.Assert(column < Width, $"Column {column} is beyond the table width");
            _columns[column].Add(text);
        }

        /// <summary>
        /// Resets the title to a new one.
        /// The number of elements in the title must be the same as the number of columns.
        /// </summary>
        public void SetTitle(params string[] title)
        {
            Debug.Assert(title.Length == Width);
            _title = title;
        }

        /// <summary>
        /// Creates a nicely formatted table string with a title, a separator line and the row content.
        /// The content is aligned so that it starts at the same horizontal position in each column.
        /// The columns are separated by the `spacing` string.
        /// </summary>
        public string ToString(string spacing = "    ")
        {
            var builder = new StringBuilder();
            var maxLengths = new int[Width];

            int Max(int a, int b) => a > b ? a : b;
            
            // Get maximum width among the columns
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
                    // Take the title row into account when calculating the max value
                    maxLengths[col] = Max(maxLengths[col], _title[col].Length);

                    builder.Append(_title[col]);
                    builder.Append(' ', maxLengths[col] - _title[col].Length);
                    builder.Append(spacing);
                }
                
                maxLengths[Width - 1] = Max(maxLengths[Width - 1], _title[Width - 1].Length);
                builder.Append(_title[Width - 1]);
                builder.AppendLine();

                // Do a dashed line below the title
                for (int col = 0; col < Width; col++)
                {
                    builder.Append('-', maxLengths[col]);
                }
                // Without the spacing at the last column
                builder.Append('-',  spacing.Length * (Width - 1));

                if (Height != 0)
                {
                    builder.AppendLine();
                }
            }
            
            // Writes the other rows
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

                if (row < Height - 1)
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }
    }

    public static class TemplateFormatting
    {
        // TODO: Autogenerate these with a script
        public static void AppendLine(this ref CodeBuilder builder, string a, string b)
        {
            builder.Indent();
            builder.Append(a);
            builder.Append(b);
            builder.NewLine();
        }

        public static void AppendLine(this ref CodeBuilder builder, string a, string b, string c)
        {
            builder.Indent();
            builder.Append(a);
            builder.Append(b);
            builder.Append(c);
            builder.NewLine();
        }

        public static void AppendLine(this ref CodeBuilder builder, string a, string b, string c, string d)
        {
            builder.Indent();
            builder.Append(a);
            builder.Append(b);
            builder.Append(c);
            builder.Append(d);
            builder.NewLine();
        }

        public static void AppendLine(this ref CodeBuilder builder, string a, string b, string c, string d, string e)
        {
            builder.Indent();
            builder.Append(a);
            builder.Append(b);
            builder.Append(c);
            builder.Append(d);
            builder.Append(e);
            builder.NewLine();
        }

        public static void AppendLine(this ref CodeBuilder builder, string a, string b, string c, string d, string e, string f)
        {
            builder.Indent();
            builder.Append(a);
            builder.Append(b);
            builder.Append(c);
            builder.Append(d);
            builder.Append(e);
            builder.Append(f);
            builder.NewLine();
        }

         public static void AppendLine(this ref CodeBuilder builder, string a, string b, string c, string d, string e, string f, string g)
        {
            builder.Indent();
            builder.Append(a);
            builder.Append(b);
            builder.Append(c);
            builder.Append(d);
            builder.Append(e);
            builder.Append(f);
            builder.Append(g);
            builder.NewLine();
        }
        // // God the stack memory things are so cumbersome in this language
        // public static unsafe void FormattedAppend<T>(
        //     this ref T builder, ReadOnlySpan<char> format, 
        //     ReadOnlySpan<string> names, 
        //     ReadOnlySpan<string> values) 
            
        //     where T : struct, IAppendable
        // {
        //     // Temporary implementation
        //     Assert(names.Length == values.Length);
        //     for (int i = 0; i < names.Length; i++)
        //     {
        //         Span<char> name = stackalloc char[3 + names[i].Length];
        //         name[0] = '$';
        //         name[1] = '(';
        //         names[i].AsSpan().CopyTo(name.Slice(2, names[i].Length));
        //         name[^1] = ')';

        //         format = format.Replace(name., values[i].AsSpan());
        //     }
        //     builder.Append(format);
        // }

        public static void FormattedAppend<T>(this ref T builder, string format, params string[] namesAndValues) 
            where T : struct, IAppend
        {
            // Temporary implementation
            for (int i = 0; i < namesAndValues.Length; i += 2)
                format = format.Replace("$(" + namesAndValues[i] + ")", namesAndValues[i + 1]);
            builder.Append(format);
        }
    }
}
