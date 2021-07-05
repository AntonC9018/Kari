using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Kari.GeneratorCore 
{
    public abstract class CodePrinterBase 
    {
        private StringBuilder builder = new StringBuilder();
        private Dictionary<string, object> session = new Dictionary<string, object>();
        private CompilerErrorCollection errors = new CompilerErrorCollection();
        private string currentIndent = string.Empty;
        private int indent = 0;
        public const int SpacesPerIndent = 2;

        private ToStringInstanceHelper _toStringHelper = new ToStringInstanceHelper();
        
        public virtual IDictionary<string, object> Session => session;
        public StringBuilder GenerationEnvironment 
        {
            get => builder;
            set { if (value == null) this.builder.Clear(); else this.builder = value; }
        }
        protected CompilerErrorCollection Errors => errors;
        public string CurrentIndent => currentIndent;
        private int Indent => indent;
        public ToStringInstanceHelper ToStringHelper => this._toStringHelper;
        
        public void Error(string message) 
        {
            Errors.Add(new CompilerError(null, -1, -1, null, message));
        }
        
        public void Warning(string message) 
        {
            CompilerError val = new CompilerError(fileName: null, line: -1, column: -1, errorNumber: null, message);
            val.IsWarning = true;
            Errors.Add(val);
        }
        
        public void PopIndent(int amount = 1) 
        {
            Debug.Assert(amount > 0);
            int lastPos = currentIndent.Length - amount * SpacesPerIndent;
            Debug.Assert(lastPos >= 0);
            currentIndent = currentIndent.Substring(0, lastPos);
        }
        
        public void PushIndent(int amount = 1) 
        {
            Debug.Assert(amount > 0);
            int increaseAmount = amount * SpacesPerIndent;
            indent += increaseAmount;

            // Add increaseAmount spaces
            currentIndent += new String(' ', increaseAmount);
        }
        
        public void ClearIndent() 
        {
            currentIndent = string.Empty;
            indent = 0;
        }
        
        public void Write(string textToAppend) 
        {
            GenerationEnvironment.Append(textToAppend.Replace("\r\n", "\r\n" + currentIndent));
        }

        public void WriteWithIndent(string text)
        {
            PushIndent();
            Write(text);
            PopIndent();
        }

        public void WriteCommentedOutIf(bool condition, string text)
        {
            Write(CurrentIndent);

            if (condition) 
            {
                Write("// ");
            }
            
            Write(text);
        }
        
        public void Write(string format, params object[] args) 
        {
            GenerationEnvironment.AppendFormat(format, args);
        }
        
        public void WriteLine(string textToAppend) 
        {
            GenerationEnvironment.Append(currentIndent);
            GenerationEnvironment.AppendLine(textToAppend);
        }
        
        public void WriteLine(string format, params object[] args) 
        {
            GenerationEnvironment.Append(currentIndent);
            GenerationEnvironment.AppendFormat(format, args);
            GenerationEnvironment.AppendLine();
        }

        public void WriteLines(IEnumerable<string> strings)
        {
            foreach (var s in strings)
            {
                GenerationEnvironment.Append(currentIndent);
                GenerationEnvironment.AppendLine(s);
            }
        }

        public void WriteLinesCommaSeparated(IEnumerable<string> strings)
        {
            PushIndent();
            GenerationEnvironment.Append(String.Join($",\n{currentIndent}", strings));
            PopIndent();
        }

        public virtual string TransformText()
        {
            return string.Empty;
        }

        // TODO: do stream writes
        public void WriteToFile(string fileName)
        {
            File.WriteAllText(fileName, TransformText(), Encoding.UTF8);
        }

        public virtual void Initialize(){}
        
        public class ToStringInstanceHelper 
        {
            private IFormatProvider formatProvider = System.Globalization.CultureInfo.InvariantCulture;
            public IFormatProvider FormatProvider => formatProvider;
            
            public string ToStringWithCulture(object objectToConvert) 
            {
                if (objectToConvert is null) 
                {
                    throw new ArgumentNullException("objectToConvert");
                }
                Type type = objectToConvert.GetType();
                Type iConvertibleType = typeof(IConvertible);
                
                if (objectToConvert is IConvertible convertible) 
                {
                    return convertible.ToString(formatProvider);
                }

                var methInfo = type.GetMethod("ToString", new Type[] {iConvertibleType});
                
                if (!(methInfo is null)) 
                {
                    return ((string)(methInfo.Invoke(objectToConvert, new object[] {
                                this.formatProvider})));
                }

                return objectToConvert.ToString();
            }
        }
    }
}