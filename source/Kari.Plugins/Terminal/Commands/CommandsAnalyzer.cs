using System.Collections.Generic;
using System.Text;
using Kari.GeneratorCore;
using Kari.GeneratorCore.Workflow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kari.Plugins.Terminal
{
    public partial class CommandsAnalyzer : IAnalyzer
    {
        private readonly HashSet<string> _names = new HashSet<string>();
        public readonly List<CommandMethodInfo> _infos = new List<CommandMethodInfo>();
        public readonly List<FrontCommandMethodInfo> _frontInfos = new List<FrontCommandMethodInfo>();
        private Logger _logger;

        private void RegisterCommandName(string name)
        {
            if (!_names.Add(name.ToUpper()))
            {
                _logger.LogError($"Duplicate command {name}");
            }
        }

        public void Collect(ProjectEnvironment environment)
        {
            _logger = environment.Logger;
            foreach (var method in environment.MethodsWithAttributes)
            {
                if (!method.IsStatic) continue;
                
                if (method.TryGetAttribute(CommandSymbols.CommandAttribute, out var commandAttribute))
                {
                    var info = new CommandMethodInfo(method, commandAttribute);
                    info.Collect(environment);
                    _infos.Add(info);
                    RegisterCommandName(info.Name);
                }
                
                if (method.TryGetAttribute(CommandSymbols.FrontCommandAttribute, out var frontCommandAttribute))
                {
                    var info = new FrontCommandMethodInfo(method, frontCommandAttribute);
                    _frontInfos.Add(info);
                    RegisterCommandName(info.Name);
                }
            }
        }

        public void InitializeParsers()
        {
            for (int i = 0; i < _infos.Count; i++)
            {
                _infos[i].InitializeParsers();
            }
        }

        public string GetClassName(ICommandMethodInfo info)
        {
            string candidate;

            if (info.Name.IndexOf('.') != -1)
            {
                candidate = info.Name.Replace(".", "_");

                if (_names.Contains(candidate.ToUpper()))
                {
                    _logger.LogWarning($"Potentially ambiguous command names: {info.Name} and {candidate}");
                }
            }
            else
            {
                candidate = info.Name;
            }

            return candidate + "Command";
        }

        public string TransformFrontCommand(FrontCommandMethodInfo info, string initialIndentation = "")
        {
            var builder = new CodeBuilder(indentation: "    ", initialIndentation);
            var className = GetClassName(info);
            builder.AppendLine($"public class {className} : CommandBase");
            builder.StartBlock();
            builder.AppendLine($"public override void Execute(CommandContext context) => {info.Symbol.GetFullyQualifiedName()}(context);");
            builder.AppendLine($"public {className}() : base({info.Attribute.MinimumNumberOfArguments}, {info.Attribute.MaximumNumberOfArguments}, \"{info.Attribute.Help}\") {{}}");
            builder.EndBlock();

            return builder.ToString();
        }

        public string TransformCommand(CommandMethodInfo info, string initialIndentation = "")
        {
            var classBuilder = new CodeBuilder(indentation: "    ", initialIndentation);
            var className = GetClassName(info);
            classBuilder.AppendLine($"public class {className} : CommandBase");
            classBuilder.StartBlock();

            var executeBuilder = classBuilder.NewWithPreservedIndentation();
            executeBuilder.AppendLine("public override void Execute(CommandContext context)");
            executeBuilder.StartBlock();

            List<OptionInfo> options = info.Options;
            List<ArgumentInfo> positionalArguments = info.PositionalArguments;
            List<ArgumentInfo> optionLikeArguments = info.OptionLikeArguments;

            var usageBuilder = new StringBuilder();
            var argsBuilder = new EvenTableBuilder("Argument/Option", "Type", "Description");
            
            usageBuilder.Append($"Usage: {info.Name} ");
            for (int i = 0; i < positionalArguments.Count; i++)
            {
                var arg = positionalArguments[i];
                usageBuilder.Append($"{arg.Name} ");
                
                argsBuilder.Append(column: 0, arg.Symbol.Name);
                argsBuilder.Append(column: 1, arg.Parser.Name);
                argsBuilder.Append(column: 2, arg.Attribute.Help);
            }

            for (int i = 0; i < optionLikeArguments.Count; i++)
            {
                var arg = optionLikeArguments[i];
                usageBuilder.Append($"{arg.Attribute.Name}|-{arg.Attribute.Name}=value ");

                argsBuilder.Append(column: 0, $"{arg.Attribute.Name}|-{arg.Attribute.Name}");
                argsBuilder.Append(column: 1, arg.Parser.Name);
                argsBuilder.Append(column: 2, arg.Attribute.Help);
            }
            
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                usageBuilder.Append($"[-{option.Name}=value] ");

                argsBuilder.Append(column: 0, "-" + option.Name);
                argsBuilder.Append(column: 1, 
                    option.Attribute.IsFlag 
                        ? $"Flag, ={option.DefaultValueText}"
                        : $"{option.Parser.Name}");
                argsBuilder.Append(column: 2, option.Attribute.Help);
            }

            var helpMessageBuilder = new StringBuilder();
            helpMessageBuilder.AppendLine(usageBuilder.ToString());
            helpMessageBuilder.AppendLine();
            if (!string.IsNullOrEmpty(info.CommandAttribute.Help))
            {
                helpMessageBuilder.AppendLine(info.CommandAttribute.Help);
                helpMessageBuilder.AppendLine();
            }
            helpMessageBuilder.AppendLine(argsBuilder.ToString());

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
                    var parserText = positionalArguments[i].Parser.FullName;

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
                executeBuilder.Append(info.Symbol.GetFullyQualifiedName() + "(");
            }
            else
            {
                executeBuilder.Append($"context.Log({info.Symbol.GetFullyQualifiedName()}(");
            }

            var parameters = new ListBuilder(", ");

            for (int i = 0; i < positionalArguments.Count; i++)
            {
                parameters.Append($"{positionalArguments[i].Symbol.Name} : __{positionalArguments[i].Name}");
            }

            for (int i = 0; i < optionLikeArguments.Count; i++)
            {
                parameters.Append($"{optionLikeArguments[i].Symbol.Name} : __{optionLikeArguments[i].Name}");
            }

            for (int i = 0; i < options.Count; i++)
            {
                parameters.Append($"{options[i].Symbol.Name} : __{options[i].Name}");
            }

            executeBuilder.Append(parameters.ToString());
            executeBuilder.Append(")");

            if (!info.Symbol.ReturnsVoid)
            {
                executeBuilder.Append(".ToString())");
            }
            executeBuilder.Append(";");
            executeBuilder.AppendLine();

            executeBuilder.EndBlock();
            
            classBuilder.AppendLine($"public {className}() : base(_MinimumNumberOfArguments, _MaximumNumberOfArguments, \"{info.CommandAttribute.Help}\", _HelpMessage) {{}}");
            classBuilder.Indent();
            classBuilder.Append("public const string _HelpMessage = @\"");
            classBuilder.Append(helpMessageBuilder.ToString().Replace("\"", "\"\""));
            classBuilder.Append("\";");
            classBuilder.AppendLine();
            // TODO: Allow default values for arguments
            classBuilder.AppendLine($"public const int _MinimumNumberOfArguments = {positionalArguments.Count};");
            classBuilder.AppendLine($"public const int _MaximumNumberOfArguments = {positionalArguments.Count + optionLikeArguments.Count};");
            classBuilder.AppendLine();
            classBuilder.Append(executeBuilder.ToString());
            classBuilder.AppendLine();
            classBuilder.EndBlock();

            return classBuilder.ToString();
        }
    }

    public interface ICommandMethodInfo
    {
        string Name { get; }
    }
    
    public class FrontCommandMethodInfo : ICommandMethodInfo
    {
        public readonly IMethodSymbol Symbol;
        public readonly FrontCommandAttribute Attribute;

        public FrontCommandMethodInfo(IMethodSymbol symbol, FrontCommandAttribute frontCommandAttribute)
        {
            Symbol = symbol;
            Attribute = frontCommandAttribute;
            frontCommandAttribute.Name ??= symbol.Name;
        }

        public string Name => Attribute.Name;
    }

    public class CommandMethodInfo : ICommandMethodInfo
    {
        public readonly IMethodSymbol Symbol;
        public readonly CommandAttribute CommandAttribute;
        public readonly List<ArgumentInfo> PositionalArguments;
        public readonly List<ArgumentInfo> OptionLikeArguments;
        public readonly List<OptionInfo> Options;

        public string Name => CommandAttribute.Name;

        public CommandMethodInfo(IMethodSymbol symbol, CommandAttribute commandAttribute)
        {
            Symbol = symbol;
            CommandAttribute = commandAttribute;
            commandAttribute.Name ??= symbol.Name;
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
                if (parameter.TryGetAttribute(CommandSymbols.ArgumentAttribute, out var argumentAttribute))
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
                if (parameter.TryGetAttribute(CommandSymbols.OptionAttribute, out var optionAttribute))
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

    public interface IArgumentInfo
    {
        IParameterSymbol Symbol { get; }
        IParserInfo Parser { get; }
        IArgument GetAttribute();
    }

    public abstract class ArgumentBase : IArgumentInfo
    {
        public IParameterSymbol Symbol { get; }
        public IParserInfo Parser { get; protected set; }
        private string _defaultValueText;
        public string DefaultValueText => (_defaultValueText is null) ? "default" : _defaultValueText;
        public bool HasDefaultValue => !(_defaultValueText is null);
        public string Name => GetAttribute().Name;
        public abstract IArgument GetAttribute();


        protected ArgumentBase(IParameterSymbol symbol)
        {
            Symbol = symbol;
            var syntax = (ParameterSyntax) symbol.DeclaringSyntaxReferences[0].GetSyntax();
            _defaultValueText = syntax.Default?.Value.ToString();
        }

        public void InitializeParser()
        {
            Parser = ParserDatabase.Instance.GetParser(this);
        }
    }

    public class ArgumentInfo : ArgumentBase, IArgumentInfo
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

    public class OptionInfo : ArgumentBase, IArgumentInfo
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
