using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Kari.GeneratorCore
{
    public enum OptionPropertyFlags
    {
        None = 0,
        Flag = 1 << 0,
        Required = 1 << 1,
        // RequiredForHelp = 1 << 2
    }

    /// <summary>
    /// Mark a field with this attibute for it to be detected and filled in by ArgumentParser.
    /// </summary>
    public class OptionAttribute : System.Attribute
    {
        public string Help;
        public OptionPropertyFlags Flags;

        private void SetFlag(OptionPropertyFlags flag, bool value)
        {
            if (value)
                Flags |= flag; 
            else 
                Flags &= ~flag; 
        }

        public bool IsFlag 
        { 
            get => (Flags & OptionPropertyFlags.Flag) != 0; 
            set => SetFlag(OptionPropertyFlags.Flag, value);
        }
        public bool IsRequired 
        { 
            get => (Flags & OptionPropertyFlags.Required) != 0; 
            set => SetFlag(OptionPropertyFlags.Required, value);
        }
        // public bool IsRequiredForHelp 
        // { 
        //     get => (Flags & OptionPropertyFlags.RequiredForHelp) != 0; 
        //     set => SetFlag(OptionPropertyFlags.RequiredForHelp, value);
        // }

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
            // public string ConfigurationFileOrigin { get; set; } 
            public ArgumentOrOptionValue(string stringValue) => StringValue = stringValue;
        }
        private readonly Dictionary<string, ArgumentOrOptionValue> Options = new Dictionary<string, ArgumentOrOptionValue>();

        private readonly struct ConfigurationFile
        {
            public readonly string Filename;
            public readonly JObject JsonRoot;

            public ConfigurationFile(string filename, JObject jsonRoot)
            {
                Filename = filename;
                JsonRoot = jsonRoot;
            }
        }
        private readonly List<ConfigurationFile> Configurations = new List<ConfigurationFile>();
        // I do not control the JObject type, neither do I control JToken.
        // If I did, I would have added a corresponding flag into the JTokenType enum instead.
        private readonly HashSet<string> TakenConfigurationOptions = new HashSet<string>();

        /// <summary>
        /// Has the help flag been passed?
        /// </summary>
        public bool IsHelpSet { get; private set; }
        public bool IsEmpty => Options.Count == 0 && Configurations.Count == 0;

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
            internal static ParsingResult InvalidConfigurationFile(string filename, string error)
                => new ParsingResult($"Invalid configuration file {filename}: {error}");
            internal static ParsingResult InvalidValueForConfigFile(string value)
                => new ParsingResult($"Invalid value for cofiguration file(s): {value}");
            internal static ParsingResult MissingValueForConfigFile()
                => new ParsingResult($"Missing value for cofiguration file(s).");
        }

        /// <summary>
        /// Goes through the given command-line arguments.
        /// Correctly fills in the internal data structure with the provided options.
        /// The wrapped error indicates the syntax error.
        /// It does not support positional arguments and double-dash options. 
        /// If it finds a "configurationJson" option, it will try to read that file. 
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

                bool isHelp = StringComparer.OrdinalIgnoreCase.Compare(option, "HELP") == 0;
                // The help is considered set even if it is given a value
                IsHelpSet = IsHelpSet || isHelp;
                
                i++;
                // If it's not followed by a value, or the value is an option, it must be a flag.
                bool isApparentlyFlag = i == arguments.Length || (arguments[i].Length > 0 && arguments[i][0] == '-');
                
                if (option == "configurationFile")
                {
                    if (isApparentlyFlag)
                        return ParsingResult.MissingValueForConfigFile(); 
                    var result = TryParseArgumentsJsons(arguments[i].Split(","));
                    i++;
                    if (result.IsError)
                        return result;
                    else
                        continue;
                }

                if (isApparentlyFlag)
                {
                    // Set the result to null.
                    // Let's leave it among the options even if it's help.
                    Options.Add(option, new ArgumentOrOptionValue(null));
                    continue;
                }

                // Record the value of help, in case the application finds it relevant.
                Options.Add(option, new ArgumentOrOptionValue(arguments[i]));
                i++;
            }

            return ParsingResult.Ok;
        }

        /// <summary>
        /// ditto.
        /// Looks for a configuration file with the given name in cwd and next to the executable.
        /// </summary>
        public ParsingResult MaybeParseConfiguration(string configurationFilename)
        {
            var jsonName = configurationFilename += ".json";
            if (Configurations.Any(conf => conf.Filename == jsonName))
                return ParsingResult.Ok;
            if (File.Exists(jsonName))
                return ParseArgumentsJson(jsonName);

            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var jsonNextToExePath = Path.Combine(exeDirectory, jsonName);
            if (Configurations.Any(conf => conf.Filename == jsonNextToExePath))
                return ParsingResult.Ok;
            if (File.Exists(jsonNextToExePath))
                return ParseArgumentsJson(jsonNextToExePath);

            return ParsingResult.Ok;
        }

        /// <summary>
        /// Reads the specified json files.
        /// The if the files do not exist.
        /// <summary>
        private ParsingResult TryParseArgumentsJsons(string[] jsonPaths)
        {
            for (int i = 0; i < jsonPaths.Length; i++)
            {
                var result = TryParseArgumentsJson(jsonPaths[i]);
                if (result.IsError)
                    return result;
            }
            return ParsingResult.Ok;
        }

        private ParsingResult TryParseArgumentsJson(string jsonPath)
        {
            if (Path.GetExtension(jsonPath) == "")
                jsonPath += ".json";
            if (Configurations.Any(conf => conf.Filename == jsonPath))
                return ParsingResult.Ok;
            if (!File.Exists(jsonPath))
                return new ParsingResult("Missing config file " + jsonPath);
            var result = ParseArgumentsJson(jsonPath);
            return result;
        }


        /// <summary>
        /// <summary>
        private ParsingResult ParseArgumentsJson(string jsonPath)
        {
            Debug.Assert(File.Exists(jsonPath));
            try
            {
                var obj = JObject.Parse(File.ReadAllText(jsonPath));
                Configurations.Add(new ConfigurationFile(jsonPath, obj));
                
                // Recurse.
                if (obj.ContainsKey("configurationFile"))
                {
                    try
                    {
                        var jsonPaths = obj["configurationFile"].Values<string>().ToArray();
                        return TryParseArgumentsJsons(jsonPaths);
                    }
                    catch(Exception){}
                    try
                    {
                        var oneJsonPath = obj["configurationFile"].ToObject<string>();
                        return TryParseArgumentsJson(oneJsonPath);
                    }
                    catch(Exception){}

                    return ParsingResult.InvalidValueForConfigFile(obj["configurationFile"].ToString());
                }
            }
            catch (Exception exception)
            {
                return ParsingResult.InvalidConfigurationFile(jsonPath, exception.Message);
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
                
                if (!hasOption)
                { 
                    bool TryGetOptionFromConfiguration(string name, out JToken confOption)
                    {
                        for (int i = 0; i < Configurations.Count; i++)
                        {
                            if (Configurations[i].JsonRoot.ContainsKey(name))
                            {
                                confOption = Configurations[i].JsonRoot[name];
                                return true;
                            }
                        }
                        confOption = null;
                        return false;
                    }
                    if (TryGetOptionFromConfiguration(name, out var optionFromConfiguration))
                    {
                        // God I hate exceptions
                        try
                        {
                            fieldInfo.SetValue(t, optionFromConfiguration.ToObject(fieldInfo.FieldType));
                            TakenConfigurationOptions.Add(name);
                        }
                        catch (Exception exception)
                        {
                            result.Errors.Add($"Cannot deserialize value for {name} into type {fieldInfo.FieldType.Name}: {exception.Message}");
                        }
                        break;
                    }
                }

                void SetValue(object value)
                {
                    fieldInfo.SetValue(t, value);
                }

                bool TrySetBoolValue()
                {
                    var comparer = StringComparer.OrdinalIgnoreCase;
                    if (comparer.Equals(option.StringValue, "TRUE"))
                    {
                        SetValue(true);
                        return true;
                    }
                    else if (comparer.Equals(option.StringValue, "FALSE"))
                    {
                        SetValue(false);
                        return true;
                    }
                    AddErrorUnknownValue();
                    return false;
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
                    // Help has the special effect that it leaves the required params unfilled
                    if (optionAttribute.IsRequired && !hasOption && !IsHelpSet)
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

                        Debug.Assert(((bool) fieldInfo.GetValue(t)) == false,
                            "The default value for {name} flag must be true");

                        if (hasOption)
                        {
                            option.IsMarked = true;
                            if (option.StringValue is null)
                            {
                                SetValue(true);
                            }
                            else if (!TrySetBoolValue())
                            {
                                result.Errors.Add($"Option {name} is a flag, you can only pass it a bool value");
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
                    TrySetBoolValue();
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
                        
                        StringBuilder toAppend = new StringBuilder();

                        void AppendProperty(string value)
                        {
                            if (toAppend.Length == 0)
                                toAppend.Append(" (");
                            else
                                toAppend.Append(", ");
                            toAppend.Append(value);
                        }

                        if (optionAttribute.IsRequired)
                            AppendProperty("required");
                        // if (optionAttribute.IsRequiredForHelp)
                        //     AppendProperty("required for help");
                        if (optionAttribute.IsFlag)
                            AppendProperty("flag");
                        if (toAppend.Length != 0)
                            toAppend.Append(")");

                        // Required things cannot have default value.
                        if (toAppend.Length == 0)
                        {
                            var value = fieldInfo.GetValue(t);

                            if (value is string[] arr)
                            {
                                toAppend.Append(" = [");
                                toAppend.Append(String.Join(",", arr));
                                toAppend.Append("]");
                            }
                            else if (value is HashSet<string> set)
                            {
                                toAppend.Append(" = [");
                                toAppend.Append(String.Join(",", set));
                                toAppend.Append("]");
                            }
                            else if (value is int[] intArr)
                            {
                                toAppend.Append(" = [");
                                toAppend.Append(String.Join(",", intArr));
                                toAppend.Append("]");
                            }
                            else
                            {
                                toAppend.Append($" = {value}");
                            }
                        }

                        sb.Append(column: 1, fieldInfo.FieldType.Name + toAppend.ToString());
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

            string GetObjectHelpMessage()
            {
                // No reason for it to be stored as a field.
                var helpMessageProperty = type.GetProperty("HelpMessage", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                if (!(helpMessageProperty is null) && !(helpMessageProperty.GetMethod is null))
                {
                    var message = helpMessageProperty.GetMethod.Invoke(t, null);
                    if (!(message is null) && message is string messageString)
                        return messageString + "\n\n";
                }
                return "";
            }

            return GetObjectHelpMessage() + sb.ToString();
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

        public readonly struct ConfigurationOption
        {
            public readonly string Filename;
            public readonly JProperty Property;

            public ConfigurationOption(string filename, JProperty property)
            {
                Filename = filename;
                Property = property;
            }

            public string GetPropertyPath() => $"File {Filename}, at {Property.Path}";
        }

        public IEnumerable<ConfigurationOption> GetUnrecognizedOptionsFromConfigurations()
        {
            var configOptionsTemp = new HashSet<string>(TakenConfigurationOptions);

            foreach (var k in Options.Keys)
            {
                configOptionsTemp.Add(k);
            }

            for (int i = 0; i < Configurations.Count; i++)
            foreach (var property in Configurations[i].JsonRoot.Properties())
            {
                if (!configOptionsTemp.Contains(property.Name))
                {
                    yield return new ConfigurationOption(Configurations[i].Filename, property);
                }
                // It should only report the property in the first file it's found.
                configOptionsTemp.Add(property.Name);
            }
        }
    }
}