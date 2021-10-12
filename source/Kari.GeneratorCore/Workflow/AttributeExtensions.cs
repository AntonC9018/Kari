using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Kari.GeneratorCore.Workflow
{
    public readonly struct AttributeSymbolWrapper<T>
    {
        public readonly INamedTypeSymbol symbol;

        private static INamedTypeSymbol GetKnownSymbol(Compilation compilation, System.Type t)
        {
            return (INamedTypeSymbol) compilation.GetTypeByMetadataName(t.FullName);
        }

        public AttributeSymbolWrapper(Compilation compilation, Logger logger)
        {
            symbol = GetKnownSymbol(compilation, typeof(T));
            if (symbol is null) logger.LogError($"{typeof(T)} not found in the compilation");
        }
    }
    
    public static class AttributeExtensions
    {
        public static T MapToType<T>(this AttributeData attributeData) where T : Attribute
        {
            T attribute;
            bool a = typeof(T).Name == "ParserAttribute";

            if (attributeData.ConstructorArguments.Length > 0 && attributeData.AttributeConstructor != null)
            {
                attribute = (T) Activator.CreateInstance(typeof(T), attributeData.GetActualConstuctorParams().ToArray());
            }
            else
            {
                attribute = (T) Activator.CreateInstance(typeof(T));
            }
            foreach (var p in attributeData.NamedArguments)
            {
                var propertyInfo = typeof(T).GetProperty(p.Key);
                if (propertyInfo != null)
                {
                    propertyInfo.SetValue(attribute, p.Value.Value);
                    continue;
                }

                var fieldInfo = typeof(T).GetField(p.Key);
                if (fieldInfo != null)
                {
                    fieldInfo.SetValue(attribute, p.Value.Value);
                    continue;
                }

                throw new Exception($"No field or property {p}");
            }
            return attribute;
        }

        public static IEnumerable<object> GetActualConstuctorParams(this AttributeData attributeData)
        {
            foreach (var arg in attributeData.ConstructorArguments)
            {
                if (arg.Kind == TypedConstantKind.Array)
                {
                    // Assume they are strings, but the array that we get from this
                    // should actually be of type of the objects within it, be it strings or ints
                    // This is definitely possible with reflection, I just don't know how exactly. 
                    // TODO
                    yield return arg.Values.Select(a => a.Value).OfType<string>().ToArray();
                }
                else
                {
                    yield return arg.Value;
                }
            }
        }
        public static bool TryGetAttributeData(this ISymbol symbol, ISymbol attributeType, out AttributeData attributeData)
        {
            var attrs = symbol.GetAttributes();
            for (int i = 0; i < attrs.Length; i++)
            {
                if (SymbolEqualityComparer.Default.Equals(attrs[i].AttributeClass, attributeType))
                {
                    attributeData = attrs[i];
                    return true;
                }
            }
            attributeData = default;
            return false;
        }

        public readonly struct AttributeConversionResult<T> where T : System.Attribute
        {
            // result null, message null     -> not success
            // result T, message null        -> good
            // result T, message not null    -> impossible
            // result null, message not null -> error
            public readonly T Result; 
            public readonly string Message;
            public bool IsError => !(Message is null) && Result is null;
            public bool IsSuccess => !(Result is null) && Message is null;
            public bool IsFail => Result is null && Message is null;

            private AttributeConversionResult(string message)
            {
                this.Result = null;
                this.Message = message;
            }

            private AttributeConversionResult(T result)
            {
                this.Result = result;
                this.Message = null;
            }

            public static AttributeConversionResult<T> Fail => new AttributeConversionResult<T>(result: null);
            public static AttributeConversionResult<T> Success(T result) =>  new AttributeConversionResult<T>(result);
            public static AttributeConversionResult<T> Error(string message) => new AttributeConversionResult<T>(message);

            public static implicit operator T(AttributeConversionResult<T> result) => result.Result;
        }

        public static AttributeConversionResult<T> GetAttributeConversionResult<T>(this ISymbol symbol, AttributeSymbolWrapper<T> attributeSymbolWrapper) where T : System.Attribute
        {
            if (TryGetAttributeData(symbol, attributeSymbolWrapper.symbol, out var attributeData))
            {
                T attribute;
                try
                {
                    attribute = attributeData.MapToType<T>();
                }
                catch (Exception exception)
                {
                    return AttributeConversionResult<T>.Error(exception.Message);
                }
                return AttributeConversionResult<T>.Success(attribute);
            }
            return AttributeConversionResult<T>.Fail;
        }

        public static bool TryGetAttribute<T>(this ISymbol symbol, AttributeSymbolWrapper<T> attributeSymbolWrapper, Logger logger, out T attribute) where T : System.Attribute
        {
            var result = GetAttributeConversionResult<T>(symbol, attributeSymbolWrapper);
            if (result.IsError) logger.LogError($"Invalid attribute usage at {symbol.Name}: {result.Message}");
            attribute = result.Result;
            return result.IsSuccess;
        }

        public static IEnumerable<T> GetAttributes<T>(this ISymbol symbol, AttributeSymbolWrapper<T> attributeSymbolWrapper) where T : System.Attribute
        {
            var attributes = symbol.GetAttributes();
            for (int i = 0; i < attributes.Length; i++)
            {
                if (SymbolEqualityComparer.Default.Equals(attributes[i].AttributeClass, attributeSymbolWrapper.symbol))
                {
                    yield return attributes[i].MapToType<T>();
                }
            }
        }

        public static bool HasAttribute(this ISymbol symbol, ISymbol attributeType)
        {
            foreach (var a in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType))
                {
                    return true;
                }
            }
            return false;
        }
    }
}