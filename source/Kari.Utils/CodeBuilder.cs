using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        /// <summary>
        /// A utility string builder with indentation support.
        /// </summary>
        public CodeBuilder(byte[] indentationBytes, int initialIndentationCount = 0, bool utfStringBuilderNotNested = false)
        {
            // IMPORTANT: (and also kind of a hack)
            // We will not dispose of the buffer when done building to avoid copying.
            // The buffer will have to be deallocated manually, by a call to ArrayPool<byte>.Shared.Return(arr).
            // I still provide the Dispose method tho, in the case when it's used outside plugins.
            StringBuilder = ZString.CreateUtf8StringBuilder(notNested: utfStringBuilderNotNested);
            CurrentIndentationCount = initialIndentationCount;
            IndentationBytes = indentationBytes;
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
                StringBuilder.AppendLiteral(IndentationBytes);
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

        /// <summary>
        /// </summary>
        public void AppendLiteral(ReadOnlySpan<byte> source)
        {
            StringBuilder.AppendLiteral(source);
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
            // TODO: 
            // How does this work on unix systems? 
            //
            // Should we replace the "\r\n"s in the string literals with "\n"s everywhere?
            // Or should we enforce "\r\n"s everywhere instead? (which is what I have opted for).
            //
            // The first one is weird, because the multiline string literals have windows line endings
            // on both platforms, so we'd have to replace them, allocating new strings every time,
            // or do this without allocations, but then it's going to be slower and way more involved,
            // because the API in dotnet is not flexible enough to make it nice.
            //
            // The second one is odd for Linux people, because they would probably expect normal line
            // endings, but at the same time the other source files are stored on GitHub as crlf's 
            // already (I think so), and does it even matter?
            //
            // The one thing that seems obvious to me is that we should not mix both types of new lines
            // in a single file (probably). So I opt for windows line endings everywhere for now,
            // because it is the solution with the least amount of friction.
            StringBuilder.Append("\r\n");
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
        public static readonly byte[] CommandSpaceSeparator = Encoding.UTF8.GetBytes(", ");
        public bool HasWritten;
        public readonly byte[] _separator;

        public CodeListBuilder(byte[] separator)
        {
            HasWritten = false;
            _separator = separator;
        }

        /// <summary>
        /// The separator indicates the string used to concatenate the added elements.
        /// For e.g. a list of parameters, one may use ", " as the separator, in to get e.g. "a, b, c".
        /// </summary>
        public static CodeListBuilder Create(string separator) 
        {
            return new CodeListBuilder(Encoding.UTF8.GetBytes(separator));
        }

        public void AppendSeparator(ref CodeBuilder builder)
        {
            builder.StringBuilder.AppendLiteral(_separator);
        }

        public bool MaybeAppendSeparator(ref CodeBuilder builder)
        {
            if (HasWritten)
                AppendSeparator(ref builder);
            return HasWritten;
        }

        public void MaybeAppendSeparatorOrSetHasWritten(ref CodeBuilder builder)
        {
            if (!MaybeAppendSeparator(ref builder))
                HasWritten = true;
        }

        public bool MaybeAppendNewLineAndSeparator(ref CodeBuilder builder)
        {
            if (HasWritten)
            {
                builder.NewLine();
                builder.IncreaseIndent();
                builder.Indent();
                builder.DecreaseIndent();
                AppendSeparator(ref builder);
            }
            return HasWritten;
        }

        public void MaybeAppendNewLineAndSeparatorOrSetHasWritten(ref CodeBuilder builder)
        {
            if (!MaybeAppendNewLineAndSeparator(ref builder))
                HasWritten = true;
        }

        /// <summary>
        /// Appends the given characters to the output code builder.
        /// The characters are placed on new line and with a separator, 
        /// in case when the element is not the first one.
        /// </summary>
        public void AppendOnNewLine(ref CodeBuilder builder, ReadOnlySpan<char> a)
        {
            MaybeAppendNewLineAndSeparatorOrSetHasWritten(ref builder);
            builder.Append(a);
        }

        /// <summary>
        /// Appends the characters to the output code builder.
        /// The characters will be written in a single line.
        /// It will take care to not place the separator if the list has had nothing written to it.
        /// </summary>
        public void AppendOnSameLine(ref CodeBuilder builder, ReadOnlySpan<char> a)
        {
            MaybeAppendSeparatorOrSetHasWritten(ref builder);
            builder.Append(a);
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

        public static void Append(this ref CodeBuilder builder, ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            builder.Append(a);
            builder.Append(b);
        }

        public static void AppendEscapeVerbatim(this ref CodeBuilder builder, ReadOnlySpan<byte> a)
        {
            // byte t = (byte) '\"';
            // TODO: I need Encoding.UTF8.IndexOf(a, currentIndex, t) or something like that for this, but it does not exist.
            // So temporary implementation is to just convert everything back and forth.
            builder.Append(Encoding.UTF8.GetString(a).Replace("\"", "\"\""));
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

        // TODO: This is a temporary implementation.
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
