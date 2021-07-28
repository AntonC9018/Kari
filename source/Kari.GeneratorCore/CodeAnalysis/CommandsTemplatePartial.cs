using System.Collections.Generic;
using System.Text;
using Kari.GeneratorCore.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kari.GeneratorCore
{
    public partial class CommandsTemplate
    {
        private readonly List<CommandMethodInfo> _infos;
        private readonly List<FrontCommandMethodInfo> _frontInfos;
        private readonly ParsersTemplate _parsers;

        public CommandsTemplate(ParsersTemplate parsers)
        {
            _infos = new List<CommandMethodInfo>();
            _frontInfos = new List<FrontCommandMethodInfo>();
            _parsers = parsers;
        }

        public void CollectInfo(Environment environment)
        {
            foreach (var method in environment.MethodsWithAttributes)
            {
                if (!method.IsStatic) continue;
                
                if (method.TryGetAttribute(environment.Symbols.CommandAttribute, out var commandAttribute))
                {
                    var info = new CommandMethodInfo(method, commandAttribute);
                    info.CollectInfo(environment);
                    _infos.Add(info);
                }
                
                if (method.TryGetAttribute(environment.Symbols.FrontCommandAttribute, out var frontCommandAttribute))
                {
                    var info = new FrontCommandMethodInfo(method, frontCommandAttribute);
                    _frontInfos.Add(info);
                }
            }
        }

        private string TransformFrontCommand(FrontCommandMethodInfo info,  string initialIndentation = "")
        {
            var builder = new CodeBuilder(indentation: "    ", initialIndentation);
            var className = $"{info.Name}Command";
            builder.AppendLine($"public class {className} : CommandBase");
            builder.StartBlock();
            builder.AppendLine($"public override void Execute(CommandContext context) => {info.Symbol.GetFullyQualifiedName()}(context);");
            builder.AppendLine($"public {className}() : base({info.Attribute.MinimumNumberOfArguments}, {info.Attribute.MaximumNumberOfArguments}, \"{info.Attribute.Help}\") {{}}");
            builder.EndBlock();

            return builder.ToString();
        }

        private string TransformCommand(CommandMethodInfo info, string initialIndentation = "")
        {
            var classBuilder = new CodeBuilder(indentation: "    ", initialIndentation);
            var className = $"{info.Name}Command";
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
                argsBuilder.Append(column: 1, arg.Symbol.Type.Name);
                argsBuilder.Append(column: 2, arg.Attribute.Help);
            }

            for (int i = 0; i < optionLikeArguments.Count; i++)
            {
                var arg = optionLikeArguments[i];
                usageBuilder.Append($"{arg.Attribute.Name}|-{arg.Attribute.Name}=value ");

                argsBuilder.Append(column: 0, $"{arg.Attribute.Name}|-{arg.Attribute.Name}");
                argsBuilder.Append(column: 1, arg.Symbol.Type.Name);
                argsBuilder.Append(column: 2, arg.Attribute.Help);
            }
            
            for (int i = 0; i < options.Count; i++)
            {
                var op = options[i];
                usageBuilder.Append($"[-{op.Name}=value] ");

                argsBuilder.Append(column: 0, "-" + op.Name);
                argsBuilder.Append(column: 2, op.Attribute.Help);
                argsBuilder.Append(column: 1, 
                    op.Attribute.IsFlag 
                        ? $"Flag, ={op.DefaultValueText}"
                        : $"{op.Symbol.Type.Name}");
            }

            var helpMessageBuilder = new StringBuilder();
            helpMessageBuilder.AppendLine(usageBuilder.ToString());
            helpMessageBuilder.AppendLine();
            helpMessageBuilder.AppendLine(info.CommandAttribute.Help);
            helpMessageBuilder.AppendLine();
            helpMessageBuilder.AppendLine(argsBuilder.ToString());

            executeBuilder.AppendLine("// Take in all the positional arguments");
            for (int i = 0; i < positionalArguments.Count; i++)
            {
                var name = positionalArguments[i].Name;
                var parserText = _parsers.GetParserName(positionalArguments[i]);
                executeBuilder.AppendLine($"var __{name} = context.ParseArgument({i}, \"{name}\", Parsers.{parserText});");
            }

            if (optionLikeArguments.Count > 0)
            {
                executeBuilder.AppendLine("// Take in all the option-like positional arguments");

                for (int i = 0; i < optionLikeArguments.Count; i++)
                {
                    var argumentIndex = positionalArguments.Count + i;
                    var name = optionLikeArguments[i].Attribute.Name;
                    var typeText = optionLikeArguments[i].Symbol.Type.GetFullyQualifiedName();
                    var parserText = _parsers.GetParserName(optionLikeArguments[i]);

                    executeBuilder.AppendLine($"{typeText} __{name};");
                    // The argument is present as a positional argument
                    executeBuilder.AppendLine($"if (context.Arguments.Count > {argumentIndex})");
                    executeBuilder.StartBlock();
                    executeBuilder.AppendLine($"__{name} = context.ParseArgument({argumentIndex}, \"{name}\", Parsers.{parserText});");
                    executeBuilder.EndBlock();
                    // The argument is present as an option
                    executeBuilder.AppendLine("else");
                    executeBuilder.StartBlock();

                    // Parse with default value, no option does not error out
                    if (optionLikeArguments[i].HasDefaultValue)
                    {
                        executeBuilder.AppendLine($"__{name} = context.ParseOption(\"{name}\", {optionLikeArguments[i].DefaultValueText}, Parsers.{parserText});");
                    }
                    // No option errors out
                    else
                    {
                        executeBuilder.AppendLine($"__{name} = context.ParseOption(\"{name}\", Parsers.{parserText});");
                    }

                    executeBuilder.EndBlock();
                }
            }

            if (options.Count > 0)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    var typeText = options[i].Symbol.Type.GetFullyQualifiedName();
                    var defaultValueText = options[i].Symbol.GetDefaultValueText();
                    var name = options[i].Name;

                    if (options[i].Attribute.IsFlag)
                    {
                        executeBuilder.AppendLine($"{typeText} __{name} = context.ParseFlag(\"{name}\", defaultValue: {defaultValueText});");
                    }
                    else
                    {
                        var parserText = _parsers.GetParserName(options[i]);
                        executeBuilder.AppendLine($"{typeText} __{name} = context.ParseOption(\"{name}\", defaultValue: {defaultValueText}, Parsers.{parserText});");
                    }
                }
            }

            executeBuilder.AppendLine("context.EndParsing();");

            // TODO: Add requirability to options
            executeBuilder.AppendLine("// Make sure all required parameters have been given");
            executeBuilder.AppendLine("if (context.HasErrors) return;");

            executeBuilder.AppendLine("// Call the function with correct arguments");
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

    public class FrontCommandMethodInfo
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

    public class CommandMethodInfo
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

        public void CollectInfo(Environment environment)
        {
            for (int i = 0; i < Symbol.Parameters.Length; i++)
            {
                var parameter = Symbol.Parameters[i];
                if (parameter.TryGetAttribute(environment.Symbols.ArgumentAttribute, out var argumentAttribute))
                {
                    var argInfo = new ArgumentInfo(parameter, argumentAttribute);
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
                if (parameter.TryGetAttribute(environment.Symbols.OptionAttribute, out var optionAttribute))
                {
                    Options.Add(new OptionInfo(parameter, optionAttribute));
                    continue;
                }
                PositionalArguments.Add(new ArgumentInfo(parameter, new ArgumentAttribute("")));
            }
        }
    }

    public interface IArgumentInfo
    {
        IParameterSymbol Symbol { get; }
        IArgument GetAttribute();
    }

    public abstract class ArgumentBase
    {
        protected ArgumentBase(IParameterSymbol symbol)
        {
            Symbol = symbol;
            var syntax = (ParameterSyntax) symbol.DeclaringSyntaxReferences[0].GetSyntax();
            _defaultValueText = syntax.Default?.Value.ToString();
        }

        private string _defaultValueText;
        public string DefaultValueText => (_defaultValueText is null) ? "default" : _defaultValueText;
        public bool HasDefaultValue => !(_defaultValueText is null);
        public IParameterSymbol Symbol { get; }
    }

    public class ArgumentInfo : ArgumentBase, IArgumentInfo
    {
        public ArgumentInfo(IParameterSymbol symbol, ArgumentAttribute argumentAttribute)
            : base(symbol)
        {
            Attribute = argumentAttribute;
            Attribute.Name ??= symbol.Name;
        }

        public ArgumentAttribute Attribute { get; }
        public string Name => Attribute.IsOptionLike ? Attribute.Name : Symbol.Name;

        IArgument IArgumentInfo.GetAttribute() => Attribute;
    }

    public class OptionInfo : ArgumentBase, IArgumentInfo
    {
        public OptionInfo(IParameterSymbol symbol, OptionAttribute optionAttribute)
            : base(symbol)
        {
            Attribute = optionAttribute;
            Attribute.Name ??= symbol.Name;
        }

        public OptionAttribute Attribute { get; }
        public string Name => Attribute.Name;
        IArgument IArgumentInfo.GetAttribute() => Attribute;
    }
}
