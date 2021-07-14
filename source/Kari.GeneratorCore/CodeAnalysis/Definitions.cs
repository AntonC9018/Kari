using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

#pragma warning disable SA1649 // File name should match first type name

namespace Kari.GeneratorCore.CodeAnalysis
{
    public class CommandMethodInfo
    {
        public CommandMethodInfo(IMethodSymbol symbol, CommandAttribute commandAttribute)
        {
            Symbol = symbol;
            CommandAttribute = commandAttribute;
        }

        public IMethodSymbol Symbol { get; }
        public string Name => CommandAttribute.Name;
        public CommandAttribute CommandAttribute { get; }
    }

    public interface IArgumentInfo
    {
        IParameterSymbol Symbol { get; }
        IArgument GetAttribute();
    }

    public class ArgumentInfo : IArgumentInfo
    {
        public ArgumentInfo(IParameterSymbol symbol, ArgumentAttribute argumentAttribute)
        {
            Symbol = symbol;
            Attribute = argumentAttribute;
        }

        public IParameterSymbol Symbol { get; }
        public ArgumentAttribute Attribute { get; }
        public string Name => Attribute.IsOptionLike ? Attribute.Name : Symbol.Name;

        IArgument IArgumentInfo.GetAttribute() => Attribute;
    }

    public class OptionInfo : IArgumentInfo
    {
        public OptionInfo(IParameterSymbol symbol, OptionAttribute optionAttribute)
        {
            Symbol = symbol;
            Attribute = optionAttribute;
        }

        public IParameterSymbol Symbol { get; }
        public OptionAttribute Attribute { get; }
        public string Name => Attribute.Name;
        IArgument IArgumentInfo.GetAttribute() => Attribute;
    }

    public class ParserInfo
    {
        public ParserInfo(IMethodSymbol symbol, ParserAttribute attribute)
        {
            Symbol = symbol;
            Attribute = attribute;
            Next = null;
        }

        public IMethodSymbol Symbol { get; }
        public ParserAttribute Attribute { get; }
        public ParserInfo Next { get; set; }
    }
}
