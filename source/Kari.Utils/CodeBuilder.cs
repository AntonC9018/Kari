using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Cysharp.Text;
using static System.Diagnostics.Debug;

namespace Kari.Utils
{
    public interface IAppend
    {
        void Append(ReadOnlySpan<char> text);
    }
    public interface IIndent
    {
        void Indent();
    }

    /// <summary>
    /// A utility string builder with indentation support.
    /// </summary>
    public struct CodeBuilder : IDisposable, IAppend //, IIndent
    {
        public static readonly byte[] DefaultIndentation = Encoding.UTF8.GetBytes("    ");
        public static CodeBuilder Create() { return new CodeBuilder(DefaultIndentation, 0); }
        public static CodeBuilder FromText(string text)
        {
            CodeBuilder result = Create();
            result.Append(text);
            return result;
        }        
        
        
        /// <summary>
        /// You may write to this directly.
        /// If you want to reference this field, you can do `ref codeBuilder.StringBuilder` too.
        /// </summary>
        public Utf8ValueStringBuilder StringBuilder;

        /// <summary>
        /// </summary>
        public int CurrentIndentationCount;

        /// <summary>
        /// The bytes used for indentation.
        /// One step in indentation amounts to a copy of these bytes being added to the indentation.
        /// </summary>
        public byte[] IndentationBytes { get; }

        /// Won't be needed when this gets merged.
        /// https://github.com/Cysharp/ZString/pull/71
        private string ib { get; }

        /// <summary>
        /// A utility string builder with indentation support.
        /// </summary>
        public CodeBuilder(byte[] indentationBytes, int initialIndentationCount = 0, bool utfStringBuilderNotNested = false)
        {
            // IMPORTANT: (and also kind of a hack)
            // We will not dispose of the buffer when done building to avoid copying.
            // The buffer will have to be deallocated manually, by a call to ArrayPool<byte>.Shared.Return(arr).
            // I still provide the Dispose method tho, in the case when  
            StringBuilder = ZString.CreateUtf8StringBuilder(notNested: utfStringBuilderNotNested);
            CurrentIndentationCount = initialIndentationCount;
            IndentationBytes = indentationBytes;
            ib = Encoding.UTF8.GetString(IndentationBytes);
        }

        /// <summary>
        /// Returns the underlying byte array to the array pool.
        /// Important: only call this if you're not giving the buffer (the array segment) to Kari.
        /// Otherwise the buffer could be overwritten.
        /// </summary>
        public void Dispose()
        {
            StringBuilder.Dispose();
        }

        /// <summary>
        /// Returns an empty CodeBuilder with the same indentation settings
        /// and the same current indentation.
        /// </summary>
        public CodeBuilder NewWithPreservedIndentation()
        {
            return new CodeBuilder(IndentationBytes, CurrentIndentationCount);
        }

        public ArraySegment<byte> AsArraySegment() => StringBuilder.AsArraySegment();
        public void IncreaseIndent() => CurrentIndentationCount++;
        public void DecreaseIndent() => CurrentIndentationCount--;

        /// <summary>
        /// Appends the indentation string to the output.
        /// </summary>
        public void Indent()
        {
            for (int i = 0; i < CurrentIndentationCount; i++)
                StringBuilder.Append(ib);
                // StringBuilder.Append(IndentationBytes);
        }

        /// <summary>
        /// Appends the indentation and a new line character to the output.
        /// </summary>
        public void AppendLine() 
        { 
            Indent();
            NewLine();
        }

        /// <summary>
        /// Appends the indentation, the text and a new line character to the output.
        /// </summary>
        public void AppendLine(ReadOnlySpan<char> text)
        {
            Indent();
            Append(text);
            NewLine();
        }

        /// <summary>
        /// Appends only the text to the output.
        /// </summary>
        public void Append(ReadOnlySpan<char> source)
        {
            StringBuilder.Append(source);
        }

        public void Append(ref CodeBuilder cb)
        {
            StringBuilder.Append(cb.StringBuilder);
        }

        /// <summary>
        /// Appends only new line to the output.
        /// </summary>
        public void NewLine()
        {
            StringBuilder.AppendLine();
        }

        /// <summary>
        /// Appends '{' and increases indentation.
        /// </summary>
        public void StartBlock()
        {
            AppendLine("{");
            IncreaseIndent();
        }

        /// <summary>
        /// Appends '}' and decreases indentation.
        /// </summary>
        public void EndBlock()
        {
            DecreaseIndent();
            AppendLine("}");
        }

        public void Clear()
        {
            CurrentIndentationCount = 0;
            StringBuilder.Clear();
        }
    }

    public struct CodeListBuilder
    {
        public bool HasWritten;
        public readonly byte[] _separator;
        private readonly string ib; // look at ib in CodeBuilder

        public CodeListBuilder(byte[] separator)
        {
            HasWritten = false;
            _separator = separator;
            ib = Encoding.UTF8.GetString(separator);
        }

        /// <summary>
        /// The separator indicates the string used to concatenate the added elements.
        /// For e.g. a list of parameters, one may use ", " as the separator, in to get e.g. "a, b, c".
        /// </summary>
        public static CodeListBuilder Create(string separator) 
        {
            return new CodeListBuilder(Encoding.UTF8.GetBytes(separator));
        }

        private void MaybeAppendSeparator(ref CodeBuilder builder)
        {
            if (!HasWritten)
                builder.Append(ib);
        }

        /// <summary>
        /// Appends the given characters to the output code builder.
        /// The characters are placed on new line and with a separator, 
        /// in case when the element is not the first one.
        /// </summary>
        public void AppendOnNewLine(ref CodeBuilder builder, ReadOnlySpan<char> a)
        {
            if (HasWritten)
            {
                builder.NewLine();
                builder.IncreaseIndent();
                builder.Indent();
                builder.DecreaseIndent();
                builder.Append(ib);
            }
            builder.Append(a);
            HasWritten = true;
        }

        /// <summary>
        /// Appends the characters to the output code builder.
        /// The characters will be written in a single line.
        /// It will take care to not place the separator if the list has had nothing written to it.
        /// </summary>
        public void AppendOnSameLine(ref CodeBuilder builder, ReadOnlySpan<char> a)
        {
            if (HasWritten)
            {
                builder.Append(ib);
            }
            builder.Append(a);
            HasWritten = true;
        }
    }

    /// <summary>
    /// A helper for building text lists, e.g. the parameters of a function call like "a, b, c".
    /// </summary>
    public readonly struct ListBuilder0
    {
        private readonly Utf8ValueStringBuilder _stringBuilder;
        private readonly byte[] _separator;

        /// <summary>
        /// Creates a list builder.
        /// The separator indicates the string used to concatenate the added elements.
        /// For e.g. a list of parameters, one may use ", " as the separator, in to get e.g. "a, b, c".
        /// </summary>
        public ListBuilder0(string separator)
        {
            _stringBuilder = new Utf8ValueStringBuilder();
            _separator = Encoding.UTF8.GetBytes(separator);
        }

        /// <summary>
        /// Adds the given element to the result, concatenating it using the separator.
        /// </summary>
        public void Append(string element)
        {
            _stringBuilder.Append(element);
            _stringBuilder.Append(_separator);
        }

        // public override string ToString()
        // {
        //     if (_stringBuilder.Length == 0)
        //     {
        //         return "";
        //     }
        //     return _stringBuilder.ToString(0, _stringBuilder.Length - _separator.Length);
        // }

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
        // TODO: Autogenerate these with a script.
        // TODO: Mirror all ZString helper methods.
        public static void AppendLine(this ref CodeBuilder builder, ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            builder.Indent();
            builder.Append(a);
            builder.Append(b);
            builder.NewLine();
        }

        public static void AppendLine(this ref CodeBuilder builder, ReadOnlySpan<char> a, ReadOnlySpan<char> b, ReadOnlySpan<char> c)
        {
            builder.Indent();
            builder.Append(a);
            builder.Append(b);
            builder.Append(c);
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
