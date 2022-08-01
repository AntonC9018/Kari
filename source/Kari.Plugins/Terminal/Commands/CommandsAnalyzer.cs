using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Text;
using Humanizer;
using Kari.GeneratorCore.Workflow;
using Kari.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kari.Plugins.Terminal
{
    internal partial class CommandsAnalyzer : ICollectSymbols, IGenerateCode
    {
        private readonly HashSet<string> _names = new HashSet<string>();
        public readonly List<CommandMethodInfo> _infos = new List<CommandMethodInfo>();
        public readonly List<FrontCommandMethodInfo> _frontInfos = new List<FrontCommandMethodInfo>();
        private NamedLogger _logger;

        private void RegisterCommandName(string name)
        {
            if (!_names.Add(name.ToUpper()))
            {
                _logger.LogError($"Duplicate command {name}");
            }
        }

        public void CollectSymbols(ProjectEnvironment environment)
        {
            _logger = environment.Logger;

            foreach (var method in environment.MethodsWithAttributes)
            {
                if (!method.IsStatic) continue;
                
                if (method.TryGetCommandAttribute(environment.Compilation, out var commandAttribute))
                {
                    var info = new CommandMethodInfo(method, commandAttribute, environment.Data.GeneratedNamespaceName);
                    info.Collect(environment);
                    _infos.Add(info);
                    RegisterCommandName(info.Name);
                }
                
                if (method.TryGetFrontCommandAttribute(environment.Compilation, out var frontCommandAttribute))
                {
                    var info = new FrontCommandMethodInfo(method, frontCommandAttribute, environment.Data.GeneratedNamespaceName);
                    _frontInfos.Add(info);
                    RegisterCommandName(info.Name);
                }
            }
        }

        internal void InitializeParsers()
        {
            for (int i = 0; i < _infos.Count; i++)
            {
                _infos[i].InitializeParsers();
            }
        }

        internal string GetClassName(ICommandMethodInfo info)
        {
            if (info.IsEscapedClassName)
            {
                if (_names.Contains(info.ClassName.ToUpper()))
                {
                    _logger.LogWarning($"Potentially ambiguous command names: {info.Name} and {info.ClassName}");
                }
            }

            return info.ClassName;
        }

        private void TransformFrontCommand(ref CodeBuilder builder, FrontCommandMethodInfo info)
        {
            var className = GetClassName(info);
            builder.AppendLine($"public class {className} : CommandBase");
            builder.StartBlock();
            builder.AppendLine($"public override void Execute(CommandContext context) => {info.Symbol.GetFullyQualifiedName()}(context);");
            builder.AppendLine($"public {className}() : base({info.Attribute.MinimumNumberOfArguments}, {info.Attribute.MaximumNumberOfArguments}, {info.Attribute.ShortHelp.AsVerbatimSyntax()}, {info.Attribute.Help.AsVerbatimSyntax()}) {{}}");
            builder.EndBlock();
        }

        public void TransformCommand(ref CodeBuilder classBuilder, CommandMethodInfo info)
        {
            var className = GetClassName(info);
            classBuilder.AppendLine($"public class {className} : CommandBase");
            classBuilder.StartBlock();

            var executeBuilder = classBuilder.NewWithPreservedIndentation();
            executeBuilder.AppendLine("public override void Execute(CommandContext context)");
            executeBuilder.StartBlock();

            List<OptionInfo> options = info.Options;
            List<ArgumentInfo> positionalArguments = info.PositionalArguments;
            List<ArgumentInfo> optionLikeArguments = info.OptionLikeArguments;

            using var usageBuilder = ZString.CreateUtf8StringBuilder(notNested: true);
            
            // TODO: this info should be available at runtime for it to print the thing.
            // It should not be in the help message, because then the format is bad.
            // var header = new string[3] { "Argument/Option", "Type", "Description" };
            // var argsBuilder = new Table();
            // argsBuilder.AddColumns(header);

            usageBuilder.AppendFormat("Usage: {0} ", info.Name);
            for (int i = 0; i < positionalArguments.Count; i++)
            {
                var arg = positionalArguments[i];
                usageBuilder.Append(arg.Name);
                usageBuilder.Append(" ");

                // row[0] = arg.Symbol.Name;
                // row[1] = arg.Parser.Name;
                // row[2] = arg.Attribute.Help;
                // argsBuilder.AddRow(row);
            }

            for (int i = 0; i < optionLikeArguments.Count; i++)
            {
                var arg = optionLikeArguments[i];
                usageBuilder.AppendFormat(" {0}|-{1}=value", arg.Attribute.Name, arg.Attribute.Name);

                // row[0] = $"{arg.Attribute.Name}|-{arg.Attribute.Name}";
                // row[1] = arg.Parser.Name;
                // row[2] = arg.Attribute.Help;
                // argsBuilder.AddRow(row);
            }
            
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                usageBuilder.Append($" [-{option.Name}=value]");

                string typeString;
                if (option.Attribute.IsFlag)
                {
                    // Not the default boolean
                    if (option.Parser is CustomParserInfo customParser)
                    {
                        typeString = $"Flag: {customParser.Name}";
                    }
                    // Default boolean parser
                    else
                    {
                        typeString = "Flag";
                    }
                }
                else
                {
                    typeString = option.Parser.Name;
                }

                if (option.HasDefaultValue)
                {
                    typeString += $", ={option.DefaultValueText}";
                }

                // argsBuilder.Append(column: 0, "-" + option.Name);
                // argsBuilder.Append(column: 1, typeString);
                // argsBuilder.Append(column: 2, option.Attribute.Help);
            }

            var helpMessageBuilder = usageBuilder;
            helpMessageBuilder.AppendLine();
            if (!string.IsNullOrEmpty(info.Attribute.Help))
            {
                helpMessageBuilder.AppendLine(info.Attribute.Help);
                helpMessageBuilder.AppendLine();
            }
            // helpMessageBuilder.AppendLine(argsBuilder.ToString());

            executeBuilder.AppendLine("// Take in all the positional arguments.");
            for (int i = 0; i < positionalArguments.Count; i++)
            {
                var name = positionalArguments[i].Name;
                var parserText = positionalArguments[i].Parser.FullName;
                executeBuilder.AppendLine($"var __{name} = context.ParseArgument({i}, \"{name}\", {parserText});");
            }

            if (optionLikeArguments.Count > 0)
            {
                executeBuilder.AppendLine("// Take in all the option-like positional arguments.");

                for (int i = 0; i < optionLikeArguments.Count; i++)
                {
                    var argumentIndex = positionalArguments.Count + i;
                    var name = optionLikeArguments[i].Attribute.Name;
                    var typeText = optionLikeArguments[i].Symbol.Type.GetFullyQualifiedName();
                    var parserText = optionLikeArguments[i].Parser.FullName;

                    executeBuilder.AppendLine($"{typeText} __{name};");
                    // The argument is present as a positional argument
                    executeBuilder.AppendLine($"if (context.Arguments.Count > {argumentIndex})");
                    executeBuilder.StartBlock();
                    executeBuilder.AppendLine($"__{name} = context.ParseArgument({argumentIndex}, \"{name}\", {parserText});");
                    executeBuilder.EndBlock();
                    // The argument is present as an option
                    executeBuilder.AppendLine("else");
                    executeBuilder.StartBlock();

                    // Parse with default value, no option does not error out
                    if (optionLikeArguments[i].HasDefaultValue)
                    {
                        executeBuilder.AppendLine($"__{name} = context.ParseOption(\"{name}\", {optionLikeArguments[i].DefaultValueText}, {parserText});");
                    }
                    // No option errors out
                    else
                    {
                        executeBuilder.AppendLine($"__{name} = context.ParseOption(\"{name}\", {parserText});");
                    }

                    executeBuilder.EndBlock();
                }
            }

            if (options.Count > 0)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    var typeText = option.Symbol.Type.GetFullyQualifiedName();
                    var defaultValueText = option.DefaultValueText;
                    var name = option.Name;

                    if (option.Attribute.IsFlag)
                    {
                        // We know flag types are bool
                        // If a custom parser is used, we must pass it to the function
                        if (option.Parser is CustomParserInfo customParser)
                        {
                            executeBuilder.AppendLine($"{typeText} __{name} = context.ParseFlag(\"{name}\", defaultValue: {defaultValueText}, parser: {customParser.FullName});");
                        }
                        // A default parser bool is used
                        else
                        {
                            executeBuilder.AppendLine($"{typeText} __{name} = context.ParseFlag(\"{name}\", defaultValue: {defaultValueText});");
                        }
                    }
                    else
                    {
                        executeBuilder.AppendLine($"{typeText} __{name} = context.ParseOption(\"{name}\", defaultValue: {defaultValueText}, {option.Parser.FullName});");
                    }
                }
            }

            executeBuilder.AppendLine("context.EndParsing();");

            // TODO: Add requirability to options
            executeBuilder.AppendLine("// Make sure all required parameters have been given.");
            executeBuilder.AppendLine("if (context.HasErrors) return;");

            executeBuilder.AppendLine("// Call the function with correct arguments.");
            executeBuilder.Indent();
            if (info.Symbol.ReturnsVoid)
            {
                executeBuilder.Append(info.Symbol.GetFullyQualifiedName(), "(");
            }
            else
            {
                executeBuilder.Append($"context.Log({info.Symbol.GetFullyQualifiedName()}(");
            }

            var parameters = CodeListBuilder.Create(", ");

            for (int i = 0; i < positionalArguments.Count; i++)
            {
                parameters.AppendOnNewLine(ref executeBuilder, 
                    $"{positionalArguments[i].Symbol.Name} : __{positionalArguments[i].Name}");
            }

            for (int i = 0; i < optionLikeArguments.Count; i++)
            {
                parameters.AppendOnNewLine(ref executeBuilder, 
                    $"{optionLikeArguments[i].Symbol.Name} : __{optionLikeArguments[i].Name}");
            }

            for (int i = 0; i < options.Count; i++)
            {
                parameters.AppendOnNewLine(ref executeBuilder, 
                    $"{options[i].Symbol.Name} : __{options[i].Name}");
            }

            executeBuilder.Append(")");

            if (!info.Symbol.ReturnsVoid)
            {
                executeBuilder.Append(".ToString())");
            }
            executeBuilder.Append(";");
            executeBuilder.AppendLine();

            executeBuilder.EndBlock();
            
            classBuilder.AppendLine($"public {className}() : base(_MinimumNumberOfArguments, _MaximumNumberOfArguments, {info.Attribute.ShortHelp.AsVerbatimSyntax()}, _HelpMessage) {{}}");
            classBuilder.Indent();
            classBuilder.Append("public const string _HelpMessage = @\"");
            classBuilder.AppendEscapeVerbatim(helpMessageBuilder.AsArraySegment());
            classBuilder.Append("\";");
            classBuilder.AppendLine();
            // TODO: Allow default values for arguments
            classBuilder.AppendLine($"public const int _MinimumNumberOfArguments = {positionalArguments.Count};");
            classBuilder.AppendLine($"public const int _MaximumNumberOfArguments = {positionalArguments.Count + optionLikeArguments.Count};");
            classBuilder.AppendLine();
            classBuilder.AppendLiteral(executeBuilder.AsArraySegment());
            classBuilder.AppendLine();
            classBuilder.EndBlock();
        }

        const string basics = @"/// <summary>
    /// The CommandContext class must implement the following duck interface.
    /// The generated commands will reference the actual CommandContext class instead, 
    /// so the interface must only be used for reference.
    /// </summary>
    public interface IDuckCommandContext
    {
        bool HasErrors { get; }
        T ParseArgument<T>(int index, string name, IValueParser<T> parser);
        bool ParseFlag(string name, bool defaultValue = false, bool flagValue = true);
        T ParseOption<T>(string name, IValueParser<T> parser);
        T ParseOption<T>(string name, T defaultValue, IValueParser<T> parser);
        void EndParsing();
    }

    /// <summary>
    /// This struct is used in a sort of main function to set the builtin commands below.
    /// </summary>
    public readonly struct CommandInfo
    {
        public readonly string Name;
        public readonly CommandBase Command;
        public CommandInfo(string name, CommandBase command)
        {
            Name = name;
            Command = command;
        }
    }

    /// <summary>
    /// Defines the data model of any reasonable command.
    /// </summary>
    public abstract class CommandBase
    {
        public abstract void Execute(CommandContext context);
        public readonly int MinimumNumberOfArguments;
        public readonly int MaximumNumberOfArguments; // TODO: Never used, should be removed?
        public readonly string HelpMessage;
        public readonly string ExtendedHelpMessage;

        protected CommandBase(int minimumNumberOfArguments, int maximumNumberOfArguments, string helpMessage, string extendedHelpMessage)
        {
            MinimumNumberOfArguments = minimumNumberOfArguments;
            MaximumNumberOfArguments = maximumNumberOfArguments;
            HelpMessage = helpMessage;
            ExtendedHelpMessage = extendedHelpMessage;
        }

        protected CommandBase(int minimumNumberOfArguments, int maximumNumberOfArguments, string helpMessage)
        {
            MinimumNumberOfArguments = minimumNumberOfArguments;
            MaximumNumberOfArguments = maximumNumberOfArguments;
            HelpMessage = helpMessage;
            ExtendedHelpMessage = helpMessage;
        }
    }

    /// <summary>
    /// Contains the default commands.
    /// Warning! This data must be set by an outside script prior to being used.
    /// You must manually set this array to the correct value.
    /// </summary>
    public static class Commands
    {
        public static CommandInfo[] BuiltinCommands;
    }
";

        internal static CodeBuilder TransformBasics()
        {
            var builder = CodeBuilder.Create();
            builder.AppendLine("namespace ", TerminalAdministrator.TerminalProject.GeneratedNamespaceName);
            builder.StartBlock();
            builder.Append(basics);
            builder.EndBlock();
            return builder;
        }

        public void GenerateCode(ProjectEnvironmentData project, ref CodeBuilder builder)
        {
            if (_infos.Count == 0 && _frontInfos.Count == 0) 
                return;

            builder.AppendLine("namespace ", project.GeneratedNamespaceName);
            builder.StartBlock();
            builder.AppendLine("using ", TerminalAdministrator.TerminalProject.Name, ";");
            builder.AppendLine("using ", TerminalAdministrator.TerminalProject.GeneratedNamespaceName, ";");

            foreach (var info in _infos) 
            { 
                TransformCommand(ref builder, info);
                builder.AppendLine();
            }

            foreach (var info in _frontInfos) 
            {
                TransformFrontCommand(ref builder, info);
                builder.AppendLine();
            }

            builder.EndBlock();
        }

        internal static CodeBuilder TransformBuiltin(ProjectEnvironmentData project, CommandsAnalyzer[] analyzers)
        {
            CodeBuilder builder = CodeBuilder.Create();
            builder.AppendLine("namespace ", project.GeneratedNamespaceName);
            builder.StartBlock();
            builder.AppendLine("public static class CommandsInitialization");
            builder.StartBlock();
            
            builder.AppendLine("public static void InitializeBuiltinCommands()");
            builder.StartBlock();
            var ns = TerminalAdministrator.TerminalProject.GeneratedNamespaceName;
            builder.AppendLine($"{ns}.Commands.BuiltinCommands = new {ns}.CommandInfo[]");
            builder.StartBlock();

            void AppendInfo(CommandsAnalyzer a, string name, string fullClassName)
            {
                builder.AppendLine($"new {ns}.CommandInfo(name: \"{name}\", command: new {fullClassName}()),");
            }

            foreach (var a in analyzers)
            {
                foreach (var info in a._infos)
                    AppendInfo(a, info.Name, info.FullClassName);
                foreach (var info in a._frontInfos)
                    AppendInfo(a, info.Name, info.FullClassName);
            }
            
            builder.DecreaseIndent();
            builder.AppendLine("};");

            builder.EndBlock();
            builder.EndBlock();
            builder.EndBlock();
            return builder;
        }
    }

    internal interface ICommandMethodInfo
    {
        string Name { get; }
        bool IsEscapedClassName { get; }
        string ClassName { get; }
        string FullClassName { get; }
        ICommandAttribute GetAttribute();
    }

    internal abstract class FrontCommandMethodInfoBase : ICommandMethodInfo
    {
        public abstract ICommandAttribute GetAttribute();
        public string Name => GetAttribute().Name;
        public bool IsEscapedClassName { get; }
        public string ClassName { get; }
        public string FullClassName { get; }

        public FrontCommandMethodInfoBase(IMethodSymbol method, ICommandAttribute attribute, string generatedNamespace)
        {
            UpdateAttributeHelp(method, attribute);

            attribute.Name ??= method.Name;
            if (attribute.Name.Contains('.'))
            {
                IsEscapedClassName = true;
                ClassName = attribute.Name.Replace('.', '_') + "Command";
            }
            else
            {
                IsEscapedClassName = false;
                ClassName = attribute.Name + "Command";
            }
            FullClassName = generatedNamespace.Join(ClassName);
        }

        private static void UpdateAttributeHelp(IMethodSymbol method, ICommandAttribute attribute)
        {
            if (!(attribute.Help is null))
            {
                return;
            }

            var xml = method.GetDocumentationXml();
            if (xml is null)
            {
                attribute.Help = "";
                return;
            }

            var root = xml.FirstChild;
            var nodes = root.ChildNodes;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i > 0) sb.AppendLine("\r\n");
                sb.Append(nodes[i].Name.Humanize(LetterCasing.Title));
                sb.Append(":");
                sb.Append(nodes[i].InnerText.TrimEnd());

                if (nodes[i].Name == "summary")
                {
                    attribute.ShortHelp = SummaryToShortHelp(nodes[i].InnerText, maxLength: 64);
                }
            }

            attribute.ShortHelp ??= "";
            attribute.Help = sb.ToString();
        }

        private static string SummaryToShortHelp(string text, int maxLength, string more = "...")
        {
            text = text.TrimStart();
            var newLineIndex = text.IndexOf("\r\n");
            if (newLineIndex == -1)
                newLineIndex = text.Length;

            if (newLineIndex > maxLength)
            {
                return text.Substring(0, maxLength - more.Length) + more;
            }
            return text.Substring(0, newLineIndex);
        }
    }
    
    internal class FrontCommandMethodInfo : FrontCommandMethodInfoBase
    {
        public readonly IMethodSymbol Symbol;
        public override ICommandAttribute GetAttribute() => Attribute;
        public readonly FrontCommandAttribute Attribute;

        public FrontCommandMethodInfo(IMethodSymbol symbol, FrontCommandAttribute frontCommandAttribute, string generatedNamespace)
            : base(symbol, frontCommandAttribute, generatedNamespace)
        {
            Symbol = symbol;
            Attribute = frontCommandAttribute;
        }
    }

    internal class CommandMethodInfo : FrontCommandMethodInfoBase
    {
        public readonly IMethodSymbol Symbol;
        public readonly CommandAttribute Attribute;
        public override ICommandAttribute GetAttribute() => Attribute;
        public readonly List<ArgumentInfo> PositionalArguments;
        public readonly List<ArgumentInfo> OptionLikeArguments;
        public readonly List<OptionInfo> Options;

        public CommandMethodInfo(IMethodSymbol symbol, CommandAttribute commandAttribute, string generatedNamespace)
            : base(symbol, commandAttribute, generatedNamespace)
        {
            Symbol = symbol;
            Attribute = commandAttribute;
            PositionalArguments = new List<ArgumentInfo>();
            OptionLikeArguments = new List<ArgumentInfo>();
            Options = new List<OptionInfo>();
        }

        public void Collect(ProjectEnvironment environment)
        {
            var paramNames = new HashSet<string>();

            void AddName(string name)
            {
                if (!paramNames.Add(name.ToUpper()))
                {
                    environment.Logger.LogError($"Duplicate parameter {name} (case-insensitive).");
                }
            }

            for (int i = 0; i < Symbol.Parameters.Length; i++)
            {
                var parameter = Symbol.Parameters[i];
                if (parameter.TryGetArgumentAttribute(environment.Compilation, out var argumentAttribute))
                {
                    var argInfo = new ArgumentInfo(parameter, argumentAttribute);
                    AddName(argInfo.Name);
                    if (!argumentAttribute.IsOptionLike)
                    {
                        PositionalArguments.Add(argInfo);
                    }
                    else
                    {
                        OptionLikeArguments.Add(argInfo);
                    }
                    continue;
                }
                // TODO: check if the name is valid (unique among options)
                if (parameter.TryGetOptionAttribute(environment.Compilation, out var optionAttribute))
                {
                    var option = new OptionInfo(parameter, optionAttribute);
                    AddName(option.Name);
                    
                    if (option.Attribute.IsFlag && option.Symbol.Type != Symbols.Bool)
                    {
                        environment.Logger.LogError($"Flag option {option.Name} in {Name} command must be a boolean.");
                    }

                    Options.Add(option);
                    continue;
                }
                var defaultInfo = new ArgumentInfo(parameter, new ArgumentAttribute(""));
                PositionalArguments.Add(defaultInfo);
                AddName(defaultInfo.Name);
            }
        }

        public void InitializeParsers()
        {
            for (int i = 0; i < PositionalArguments.Count; i++)
            {
                PositionalArguments[i].InitializeParser();
            }
            for (int i = 0; i < OptionLikeArguments.Count; i++)
            {
                OptionLikeArguments[i].InitializeParser();
            }
            for (int i = 0; i < Options.Count; i++)
            {
                Options[i].InitializeParser();
            }
        }
    }

    internal interface IArgumentInfo
    {
        IParameterSymbol Symbol { get; }
        IParserInfo Parser { get; }
        IArgument GetAttribute();
    }

    internal abstract class ArgumentBase : IArgumentInfo
    {
        public IParameterSymbol Symbol { get; }
        public IParserInfo Parser { get; protected set; }
        public readonly string DefaultValueText;
        public readonly bool HasDefaultValue;
        public string Name => GetAttribute().Name;
        public abstract IArgument GetAttribute();

        protected ArgumentBase(IParameterSymbol symbol)
        {
            Symbol = symbol;
            var syntax = (ParameterSyntax) symbol.DeclaringSyntaxReferences[0].GetSyntax();
            if (syntax.Default is null)
            {
                HasDefaultValue = false;
                DefaultValueText = symbol.GetDefaultValueText();
            }
            else
            {
                HasDefaultValue = true;
                DefaultValueText = syntax.Default.Value.ToString();
            }   
        }

        public void InitializeParser()
        {
            Parser = ParserDatabase.Instance.GetParser(this);
        }
    }

    internal class ArgumentInfo : ArgumentBase, IArgumentInfo
    {
        public ArgumentAttribute Attribute { get; }
        public override IArgument GetAttribute() => Attribute;

        public ArgumentInfo(IParameterSymbol symbol, ArgumentAttribute argumentAttribute)
            : base(symbol)
        {
            Attribute = argumentAttribute;
            Attribute.Name ??= symbol.Name;
        }
    }

    internal class OptionInfo : ArgumentBase, IArgumentInfo
    {
        public OptionAttribute Attribute { get; }
        public override IArgument GetAttribute() => Attribute;

        public OptionInfo(IParameterSymbol symbol, OptionAttribute optionAttribute)
            : base(symbol)
        {
            Attribute = optionAttribute;
            Attribute.Name ??= symbol.Name;
        }
    }
}
