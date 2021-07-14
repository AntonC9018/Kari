using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kari.GeneratorCore.CodeAnalysis
{

    public class CommandMethodPrinter
    {
        public CommandMethodInfo info;
        public RelevantSymbols symbols;
        public Dictionary<ITypeSymbol, string> converters;

        public CommandMethodPrinter(CommandMethodInfo info, RelevantSymbols symbols)
        {
            this.info = info;
            converters = new Dictionary<ITypeSymbol, string>
            {
                { symbols.Bool, "bool.Parse" },
                { symbols.Int, "int.Parse" },
                { symbols.String, "" }
            };
            this.symbols = symbols;
        }

        public string GetConverter(IParameterSymbol parameter)
        {
            return converters[(parameter.Type)];
            // if (parameter.Type.ApproximatelyEqual(symbols.String))
            // {
            //     return "";
            // }
            // if (parameter.Type is INamedTypeSymbol namedType && namedType.SpecialType != SpecialType.None)
            // {
            //     return SpecialTypes
            // }
        }

        public void Go()
        {
            var classBuilder = new CodeBuilder();
            classBuilder.AppendLine($"public class {info.Name}Command : ICommand");
            classBuilder.StartBlock();

            var executeBuilder = new CodeBuilder(classBuilder.CurrentIndentation);
            executeBuilder.AppendLine("public string Execute(CommandContext context)");
            executeBuilder.StartBlock();

            // Positional arguments: arguments that are not option-like
            // Must be the first ones.
            // Option-like arguments:
            List<OptionInfo> options = new List<OptionInfo>();
            List<ArgumentInfo> positionalArguments = new List<ArgumentInfo>();
            List<ArgumentInfo> optionLikeArguments = new List<ArgumentInfo>();

            for (int i = 0; i < info.Symbol.Parameters.Length; i++)
            {
                var parameter = info.Symbol.Parameters[i];
                if (parameter.TryGetAttribute(symbols.ArgumentAttributeWrapper, out var argumentAttribute))
                {
                    var argInfo = new ArgumentInfo(parameter, argumentAttribute);
                    if (!argumentAttribute.IsOptionLike)
                    {
                        positionalArguments.Add(argInfo);
                    }
                    else
                    {
                        optionLikeArguments.Add(argInfo);
                    }
                    continue;
                }
                if (parameter.TryGetAttribute(symbols.OptionAttributeWrapper, out var optionAttribute))
                {
                    options.Add(new OptionInfo(parameter, optionAttribute));
                }
            }

            var usageBuilder = new StringBuilder();
            var argsBuilder = new StringBuilder();
            
            usageBuilder.Append($"Usage: {info.Name} ");
            for (int i = 0; i < positionalArguments.Count; i++)
            {
                var arg = positionalArguments[i];
                usageBuilder.Append($"{arg.Name} ");
                argsBuilder.AppendLine($"{arg.Symbol.Name} ({arg.Symbol.Type.Name}): {arg.ArgumentAttribute.Help}");
            }

            for (int i = 0; i < optionLikeArguments.Count; i++)
            {
                var arg = positionalArguments[i];
                usageBuilder.Append($"{arg.ArgumentAttribute.Name}|-{arg.ArgumentAttribute.Name}=value ");
                argsBuilder.AppendLine($"{arg.ArgumentAttribute.Name}|-{arg.ArgumentAttribute.Name} ({arg.Symbol.Type.Name}): {arg.ArgumentAttribute.Help}");
            }
            
            for (int i = 0; i < options.Count; i++)
            {
                var op = options[i];
                usageBuilder.Append($"[-{op.Name}=value] ");
                if (op.OptionAttribute.IsFlag)
                {
                    argsBuilder.AppendLine($"-{op.Name} (flag, default {op.Symbol.GetDefaultValueText()}): {op.OptionAttribute.Help}");
                }
                else
                {
                    argsBuilder.AppendLine($"-{op.Name} ({op.Symbol.Type.Name}): {op.OptionAttribute.Help}");
                }
            }

            var helpMessageBuilder = new StringBuilder();
            helpMessageBuilder.AppendLine(usageBuilder.ToString());
            helpMessageBuilder.AppendLine();
            helpMessageBuilder.AppendLine(info.CommandAttribute.Help);
            helpMessageBuilder.AppendLine();
            helpMessageBuilder.AppendLine("Arguments:");
            helpMessageBuilder.AppendLine(argsBuilder.ToString());

            // If the function takes in any positional arguments, an empty input is considered help
            if (positionalArguments.Count > 0)
            {
                executeBuilder.AppendLine("if (context.Parser.IsEmpty) return HelpMessage;");
            }
            // Check for the -help flag as the first flag
            executeBuilder.AppendLine("if (ExecuteHelper.IsHelp(context.Parser)) return HelpMessage;");

            executeBuilder.AppendLine("// Take in all the positional arguments");
            for (int i = 0; i < positionalArguments.Count; i++)
            {
                var converterText = GetConverter(positionalArguments[i].Symbol);
                executeBuilder.AppendLine($"string __posInput{i} = context.Parser.GetString();");
                executeBuilder.AppendLine($"if (__posInput{i} == null)");
                executeBuilder.StartBlock();
                executeBuilder.AppendLine($"throw new Exception(\"Expected a positional argument {positionalArguments[i].Symbol.Name}\");");
                executeBuilder.EndBlock();
                executeBuilder.AppendLine($"var __posArg{i} = {converterText}(__posInput{i});");
                executeBuilder.AppendLine("context.Parser.SkipWhitespace()");
            }

            if (optionLikeArguments.Count > 0)
            {
                executeBuilder.AppendLine("// Take in all the option-like positional arguments");

                for (int i = 0; i < optionLikeArguments.Count; i++)
                {
                    executeBuilder.AppendLine($"bool __isPresentOptionLikeArg{i} = false;");
                    var typeText = positionalArguments[i].Symbol.Type.GetFullyQualifiedName();
                    var defaultValueText = positionalArguments[i].Symbol.GetDefaultValueText();
                    executeBuilder.AppendLine($"{typeText} __optionLikeArg{i} = {defaultValueText}");
                }

                executeBuilder.StartBlock();
                for (int i = 0; i < optionLikeArguments.Count; i++)
                {
                    var converterText = GetConverter(positionalArguments[i].Symbol);
                    executeBuilder.AppendLine($"var __input = context.Parser.GetString()");
                    executeBuilder.AppendLine($"if (__input is null)");
                    executeBuilder.StartBlock();
                    executeBuilder.AppendLine($"goto __afterOptionLike;");
                    executeBuilder.EndBlock();
                    executeBuilder.AppendLine("context.Parser.SkipWhitespace();");
                    executeBuilder.AppendLine($"__isPresentOptionLikeArg{i} = true;");
                    executeBuilder.AppendLine($" __optionLikeArg{i} = {converterText}(__input);");
                }
                executeBuilder.AppendLine("__afterOptionLike:");
                executeBuilder.EndBlock();
            }

            if (options.Count > 0 || optionLikeArguments.Count > 0)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    var typeText = options[i].Symbol.Type.GetFullyQualifiedName();
                    var defaultValueText = options[i].Symbol.GetDefaultValueText();
                    executeBuilder.AppendLine($"{typeText} __option{i} = {defaultValueText}");
                }

                executeBuilder.AppendLine("while (context.Parser.TryGetOption(out Option __option))");
                executeBuilder.StartBlock();
                executeBuilder.AppendLine("context.Parser.SkipWhitespace();");
                executeBuilder.AppendLine("switch (__option.Name)");
                executeBuilder.StartBlock();

                for (int i = 0; i < options.Count; i++)
                {
                    executeBuilder.AppendLine($"case {options[i].Name}:");
                    executeBuilder.StartBlock();

                    if (options[i].OptionAttribute.IsFlag)
                    {
                        executeBuilder.AppendLine($"__option{i} = __option.GetFlagValue();");
                    }
                    else
                    {
                        var converterText = GetConverter(options[i].Symbol);
                        executeBuilder.AppendLine($"__option{i} = {converterText}(__option.Value);");
                    }
                    executeBuilder.AppendLine("break;");
                    executeBuilder.EndBlock();
                }

                for (int i = 0; i < optionLikeArguments.Count; i++)
                {
                    executeBuilder.AppendLine($"case {optionLikeArguments[i].ArgumentAttribute.Name}:");
                    executeBuilder.StartBlock();

                    var converterText = GetConverter(optionLikeArguments[i].Symbol);
                    executeBuilder.AppendLine($"__optionLikeArg{i} = {converterText}(__option.Value);");
                    executeBuilder.AppendLine($"__isPresentOptionLikeArg{i} = true;");
                    executeBuilder.AppendLine("break;");
                    executeBuilder.EndBlock();
                }
                executeBuilder.AppendLine("default: throw new Exception($\"Unknown option: {__option.Name}\");");
                executeBuilder.EndBlock();
                executeBuilder.EndBlock();
            }

            // TODO: Add requirability to options
            if (optionLikeArguments.Count > 0)
            {
                executeBuilder.AppendLine("// Make sure all required parameters have been given");
                for (int i = 0; i < optionLikeArguments.Count; i++)
                {
                    executeBuilder.AppendLine($"if (!__isPresentOptionLikeArg{i})");
                    executeBuilder.StartBlock();
                    executeBuilder.AppendLine($"throw new Exception(\"Option-like argument {optionLikeArguments[i].ArgumentAttribute.Name} not given\");");
                    executeBuilder.EndBlock();
                }
            }

            executeBuilder.AppendLine("// Call the function with correct arguments");
            executeBuilder.Indent();
            if (info.Symbol.ReturnsVoid)
            {
                executeBuilder.Append(info.Symbol.GetFullyQualifiedName() + "(");
            }
            else
            {
                executeBuilder.Append($"return {info.Symbol.GetFullyQualifiedName()}(");
            }

            var parameters = new ParameterAppender(1);

            for (int i = 0; i < positionalArguments.Count; i++)
            {
                parameters.Append($"{positionalArguments[i].Symbol.Name} : __posArg{i}");
            }

            for (int i = 0; i < optionLikeArguments.Count; i++)
            {
                parameters.Append($"{optionLikeArguments[i].Symbol.Name} : __optionLikeArg{i}");
            }

            for (int i = 0; i < options.Count; i++)
            {
                parameters.Append($"{options[i].Symbol.Name} : __option{i}");
            }

            executeBuilder.Append(parameters.ToString());
            executeBuilder.Append(")");

            if (!info.Symbol.ReturnsVoid)
            {
                executeBuilder.Append(".ToString()");
            }
            executeBuilder.AppendLine(";");

            executeBuilder.EndBlock();
            
            classBuilder.Append("public string HelpMessage => @\"");
            classBuilder.Append(helpMessageBuilder.ToString().Replace("\"", "\"\""));
            classBuilder.AppendLine("\"");
            classBuilder.Append(executeBuilder.ToString());
            classBuilder.AppendLine("");
            classBuilder.EndBlock();
        }
    }

    

    public class TypeCollector
    {
        private Compilation _compilation;
        private RelevantSymbols _relevantSymbols;
        private INamespaceSymbol _rootNamespace;
        
        public TypeCollector(Compilation compilation, string rootNamespace, Action<string> logger)
        {
            _compilation = compilation;
            _relevantSymbols = new RelevantSymbols(compilation, logger);
            _rootNamespace = compilation.GetNamespace(rootNamespace);
        }

        public IEnumerable<CommandMethodInfo> GetCommandMethods()
        {
            foreach (var type in _rootNamespace.GetNotNestedTypes())
            foreach (var method in type.GetMethods())
            {
                if (method.TryGetAttribute(_relevantSymbols.CommandAttributeWrapper, out var commandAttribute))
                {
                    yield return new CommandMethodInfo(symbol: method, commandAttribute);
                }
            }
        }
    }
}
