using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Kari.GeneratorCore
{
    /// <summary>
    /// Mark a field with this attibute for it to be detected and filled in by ArgumentParser.
    /// </summary>
    public class OptionAttribute : System.Attribute
    {
        public string Help;
        public bool IsFlag { get; set; }
        public bool IsRequired { get; set; }

        public OptionAttribute(string help)
        {
            Help = help;
        }
    }

    /// <summary>
    /// Parses command-line arguments.
    /// Fills in objects with the appropriate data gathered from the arguments via reflection.
    /// </summary>
    public class ArgumentParser
    {
        private class ArgumentOrOptionValue
        {
            public readonly string StringValue;
            // Indicates whether this value has been recognized as a valid option.
            public bool IsMarked { get; set; }
            public ArgumentOrOptionValue(string stringValue) => StringValue = stringValue;
        }

        private readonly Dictionary<string, ArgumentOrOptionValue> Options = new Dictionary<string, ArgumentOrOptionValue>();

        public bool IsEmpty => Options.Count == 0;

        /// <summary>
        /// Wraps a string error.
        /// </summary>
        public struct ParsingResult
        {
            public readonly string Error;
            public bool IsError => !(Error is null);
            internal ParsingResult(string error) => Error = error;

            internal static readonly ParsingResult Ok = new ParsingResult(null);
            internal static ParsingResult DoubleDash(string argument) 
                => new ParsingResult($"Double dash is not allowed (in `{argument}`). Use single dash instead.");
            internal static ParsingResult InvalidOptionFormat(string argument) 
                => new ParsingResult($"The option `{argument}` must start with a single dash `-`.");
            internal static ParsingResult DuplicateOption(string option)
                => new ParsingResult($"Duplicate option `{option}`.");
        }

        /// <summary>
        /// Goes through the given command-line arguments.
        /// Correctly fills in the internal data structure with the provided options.
        /// The wrapped error indicates the syntax error.
        /// It does not support positional arguments and double-dash options. 
        /// </summary>
        public ParsingResult ParseArguments(string[] arguments)
        {
            int i = 0;

            while (i < arguments.Length)
            {
                // This is an option
                if (arguments[i][0] != '-')
                {
                    return ParsingResult.InvalidOptionFormat(arguments[i]);
                }

                // We do not allow "--"
                if (arguments[i][1] == '-')
                {
                    return ParsingResult.DoubleDash(arguments[i]);
                }

                string option = arguments[i].Substring(1);

                if (Options.ContainsKey(option))
                {
                    return ParsingResult.DuplicateOption(option);
                }
                
                // If it's not followed by a value, or the value is an option,
                // set the result to null.
                i++;
                if (i == arguments.Length || arguments[i][0] == '-')
                {
                    Options.Add(option, new ArgumentOrOptionValue(null));
                    continue;
                }

                Options.Add(option, new ArgumentOrOptionValue(arguments[i]));
                i++;
            }

            return ParsingResult.Ok;
        }

        /// <summary>
        /// Wraps an object-error pair.
        /// If the mapping went without type conversion errors 
        /// and without any unsupplied required arguments, IsError will be false.
        /// </summary>
        public readonly struct MappingResult
        {
            public readonly List<string> Errors;
            public bool IsError => Errors.Count > 0;
            private readonly object Result;

            public MappingResult(object t)
            {
                Errors = new List<string>();
                Result = t;
            }
        }

        /// <summary>
        /// Fills in the given object fields via reflection with the parsed options.
        /// Fields take their value from the option with exactly the same name as that field.
        /// Shortened options like `-i` are not supported.
        /// Only field types supported: string, int, bool, string[], List<string>, int[], HashSet<string>
        /// </summary>
        public MappingResult FillObjectWithOptionValues(object t)
        {
            // If the type of t is a struct, we need to box it, which is why we take an object here.
            MappingResult result = new MappingResult(t);

            var type = t.GetType();

            foreach (var fieldInfo in GetFieldInfos(type))
            foreach (var attr in fieldInfo.GetCustomAttributes(inherit: false))
            {
                if (!(attr is OptionAttribute optionAttribute))
                    continue;

                string name = fieldInfo.Name;
                bool hasOption = Options.TryGetValue(name, out var option);

                void SetValue(object value)
                {
                    fieldInfo.SetValue(t, value);
                }

                Debug.Assert(!optionAttribute.IsRequired || !optionAttribute.IsFlag,
                    $"`{name}` cannot be both flag and required");

                // Make sure it's one of the supported types.
                Debug.Assert(fieldInfo.FieldType == typeof(bool) 
                    || fieldInfo.FieldType == typeof(int) 
                    || fieldInfo.FieldType == typeof(string) 
                    || fieldInfo.FieldType == typeof(string[]) 
                    || fieldInfo.FieldType == typeof(List<string>) 
                    || fieldInfo.FieldType == typeof(int[])
                    || fieldInfo.FieldType == typeof(HashSet<string>));

                bool Validate()
                {
                    if (optionAttribute.IsRequired && !hasOption)
                    {
                        result.Errors.Add($"Missing required option: {name}");
                        return false;
                    }

                    if (!optionAttribute.IsFlag && hasOption && option.StringValue is null)
                    {
                        result.Errors.Add($"Option {name} cannot be used like a flag");
                        return false;
                    }

                    if (optionAttribute.IsFlag)
                    {
                        Debug.Assert(fieldInfo.FieldType == typeof(bool),
                            $"{name}, indicated as flag, must be bool type");

                        Debug.Assert(((bool)fieldInfo.GetValue(t)) == false,
                            "The default value for {name} flag must be true");

                        if (hasOption)
                        {
                            option.IsMarked = true;
                            if (option.StringValue is null)
                            {
                                SetValue(true);
                            }
                            else
                            {
                                result.Errors.Add($"Option {name} is a flag, you cannot pass it a value");
                            }
                        }
                        // If it's not set it's already false
                        return false;
                    }

                    return hasOption;
                }

                if (!Validate()) break;

                void AddErrorUnknownValue()
                {
                    result.Errors.Add($"Unknown value for {name} of type {fieldInfo.FieldType.FullName}: {option.StringValue}.");
                }

                int ParseAsInteger(string str)
                {
                    if (int.TryParse(str, out int value))
                    {
                        return value;
                    }
                    AddErrorUnknownValue();
                    return 0;
                }

                string[] ParseAsStringArray()
                {
                    var str = option.StringValue;
                    return str.Split(',');
                }

                option.IsMarked = true;
                if (fieldInfo.FieldType == typeof(bool))
                {
                    var comparer = StringComparer.OrdinalIgnoreCase;
                    if (comparer.Equals(option.StringValue, "TRUE"))
                    {
                        SetValue(true);
                    }
                    else if (comparer.Equals(option.StringValue, "FALSE"))
                    {
                        SetValue(false);
                    }
                    else
                    {
                        AddErrorUnknownValue();
                    }
                }
                else if (fieldInfo.FieldType == typeof(int))
                {
                    SetValue(ParseAsInteger(option.StringValue));
                }
                else if (fieldInfo.FieldType == typeof(string))
                {
                    SetValue(option.StringValue);
                }
                else if (fieldInfo.FieldType == typeof(string[]))
                {
                    SetValue(ParseAsStringArray());
                }
                else if (fieldInfo.FieldType == typeof(List<string>))
                {
                    SetValue(ParseAsStringArray().ToList());
                }
                else if (fieldInfo.FieldType == typeof(HashSet<string>))
                {
                    SetValue(ParseAsStringArray().ToHashSet());
                }
                else if (fieldInfo.FieldType == typeof(int[]))
                {
                    var arr = ParseAsStringArray();
                    var ints = new int[arr.Length];
                    for (int i = 0; i < arr.Length; i++)
                    {
                        ints[i] = ParseAsInteger(arr[i]);
                    }
                    SetValue(ints);
                }
            }

            return result;
        }

        private static FieldInfo[] GetFieldInfos(Type type)
        {
            return type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        }

        private static readonly string[] _Header = new string[] { "Option", "Type", "Description" };  

        /// <summary>
        /// Returns an even table willed with the information about options a given object takes.
        /// </summary>
        public string GetHelpFor(object t)
        {
            var type = t.GetType();
            var sb = new EvenTableBuilder(_Header);

            foreach (var fieldInfo in GetFieldInfos(type))
            foreach (var attr in fieldInfo.GetCustomAttributes(inherit: false))
            {
                if (!(attr is OptionAttribute optionAttribute))
                    continue;
                
                Debug.Assert(!string.IsNullOrEmpty(optionAttribute.Help),
                    "If it's super obvious what the option does, set the help to \" \"");

                // Split the help in manageable pieces that all could sort of wrap in the third column,
                // but we're emulating wrapping by appending spaces.
                const int chunkLength = 64;
                for (int i = 0; i < optionAttribute.Help.Length; i += chunkLength)
                {
                    if (i == 0)
                    {
                        sb.Append(column: 0, fieldInfo.Name);
                        
                        string typeName = fieldInfo.FieldType.Name;
                        string toAppend;
                        if (optionAttribute.IsRequired)
                        {
                            toAppend = " (required)";
                        }
                        else if (optionAttribute.IsFlag)
                        {
                            toAppend = " (flag)";
                        }
                        else
                        {
                            var value = fieldInfo.GetValue(t);

                            if (value is null)
                            {
                                toAppend = "";
                            }
                            else if (value is string[] arr)
                            {
                                var b = new StringBuilder(" = [");
                                b.Append(String.Join(",", arr));
                                b.Append("]");

                                toAppend = b.ToString();
                            }
                            else if (value is HashSet<string> set)
                            {
                                var b = new StringBuilder(" = [");
                                b.Append(String.Join(",", set));
                                b.Append("]");

                                toAppend = b.ToString();
                            }
                            else if (value is int[] intArr)
                            {
                                var b = new StringBuilder(" = [");
                                b.Append(String.Join(",", intArr));
                                b.Append("]");

                                toAppend = b.ToString();
                            }
                            else
                            {
                                toAppend = $" = {value}";
                            }
                        }

                        sb.Append(column: 1, typeName + toAppend);
                    }
                    else
                    {
                        // Just skip these columns
                        sb.Append(column: 0, "");
                        sb.Append(column: 1, "");
                    }
                    sb.Append(column: 2, optionAttribute.Help.Substring(i, 
                        // It may not go beyond the end
                        Math.Min(chunkLength, optionAttribute.Help.Length - i)));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns the options that have not been assigned to fields of any objects
        /// by previous calls to `FillObjectWithOptionValues()`.
        /// </summary>
        public IEnumerable<string> GetUnrecognizedOptions()
        {
            foreach (var option in Options)
            {
                if (!option.Value.IsMarked)
                {
                    yield return option.Key;
                }
            } 
        }
    }
}