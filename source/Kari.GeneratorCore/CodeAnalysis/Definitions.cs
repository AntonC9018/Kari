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

    public class ArgumentInfo
    {
        public ArgumentInfo(IParameterSymbol symbol, ArgumentAttribute argumentAttribute)
        {
            Symbol = symbol;
            ArgumentAttribute = argumentAttribute;
        }

        public IParameterSymbol Symbol { get; }
        public ArgumentAttribute ArgumentAttribute { get; }
        public string Name => ArgumentAttribute.IsOptionLike ? ArgumentAttribute.Name : Symbol.Name;
    }

    public class OptionInfo
    {
        public OptionInfo(IParameterSymbol symbol, OptionAttribute optionAttribute)
        {
            Symbol = symbol;
            OptionAttribute = optionAttribute;
        }

        public IParameterSymbol Symbol { get; }
        public OptionAttribute OptionAttribute { get; }
        public string Name => OptionAttribute.Name;
    }
}
