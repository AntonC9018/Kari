using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Kari.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using Spectre.Console.Rendering;
using static System.Diagnostics.Debug;

namespace Kari.Arguments
{
    public enum OptionPropertyFlags
    {
        None = 0,
        Flag = 1 << 0,
        Required = 1 << 1,
        Path = 1 << 2
        // RequiredForHelp = 1 << 2
    }

    /// <summary>
    /// Mark a field with this attibute for it to be detected and filled in by ArgumentParser.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
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

        public bool IsPath 
        { 
            get => (Flags & OptionPropertyFlags.Path) != 0; 
            set => SetFlag(OptionPropertyFlags.Path, value);
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
    /// Hides a given enum member (does not display it among the suggestions).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class HideOptionAttribute : System.Attribute
    {
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
            public ArgumentOrOptionValue(string stringValue)
            {
                StringValue = stringValue;
            }
        }
        private readonly Dictionary<string, ArgumentOrOptionValue> Options = new Dictionary<string, ArgumentOrOptionValue>();

        private readonly struct ConfigurationFile
        {
            public readonly string FileFullPath;
            public readonly string DirectoryFullPath;
            public readonly JObject JsonRoot;

            public ConfigurationFile(string fileFullPath, JObject jsonRoot)
            {
                FileFullPath = fileFullPath;
                DirectoryFullPath = Path.GetDirectoryName(fileFullPath);
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
        public bool IsVersionSet { get; private set; }
        public bool IsEmpty => Options.Count == 0 && Configurations.Count == 0;

        /// <summary>
        /// Wraps a string error.
        /// </summary>
        public readonly struct ParsingResult
        {
            public readonly string Error;
            public bool IsError => Error is not null;
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

                // TODO: I kinda want to allow option=value??

                string option = arguments[i].Substring(1);

                if (Options.ContainsKey(option))
                {
                    return ParsingResult.DuplicateOption(option);
                }

                bool isHelp = StringComparer.OrdinalIgnoreCase.Compare(option, "HELP") == 0;
                // The help is considered set even if it is given a value
                IsHelpSet = IsHelpSet || isHelp;

                bool isVersion = StringComparer.OrdinalIgnoreCase.Compare(option, "VERSION") == 0;
                // The help is considered set even if it is given a value
                IsVersionSet = IsVersionSet || isVersion;
                
                i++;
                // If it's not followed by a value, or the value is an option, it must be a flag.
                bool isApparentlyFlag = i == arguments.Length || (arguments[i].Length > 0 && arguments[i][0] == '-');
                
                if (option == "configurationFile")
                {
                    if (isApparentlyFlag)
                        return ParsingResult.MissingValueForConfigFile(); 
                    var result = TryParseArgumentsJsons(arguments[i].Split(','), Directory.GetCurrentDirectory());
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
        public ParsingResult MaybeParseConfiguration(string configurationFileNameWithoutExtension)
        {
            var jsonName = configurationFileNameWithoutExtension += ".json";
            if (Configurations.Any(conf => conf.FileFullPath == jsonName))
                return ParsingResult.Ok;
            if (File.Exists(jsonName))
                return ParseArgumentsJson(jsonName);

            var exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var jsonNextToExePath = Path.Join(exeDirectory, jsonName);
            if (Configurations.Any(conf => conf.FileFullPath == jsonNextToExePath))
                return ParsingResult.Ok;
            if (File.Exists(jsonNextToExePath))
                return ParseArgumentsJson(jsonNextToExePath);

            return ParsingResult.Ok;
        }

        /// <summary>
        /// Reads the specified json files.
        /// The if the files do not exist.
        /// <summary>
        private ParsingResult TryParseArgumentsJsons(string[] jsonPaths, string relativeToDirectory)
        {
            for (int i = 0; i < jsonPaths.Length; i++)
            {
                string path = FileSystem.ToFullNormalizedPath(jsonPaths[i], relativeToDirectory);
                var result = TryParseArgumentsJson(path);
                if (result.IsError)
                    return result;
            }
            return ParsingResult.Ok;
        }

        private ParsingResult TryParseArgumentsJson(string jsonFullPath)
        {
            if (Path.GetExtension(jsonFullPath) == "")
                jsonFullPath += ".json";
            jsonFullPath = FileSystem.WithNormalizedDirectorySeparators(jsonFullPath);
            if (Configurations.Any(conf => conf.FileFullPath == jsonFullPath))
                return ParsingResult.Ok;
            if (!File.Exists(jsonFullPath))
                return new ParsingResult("Missing config file " + jsonFullPath);
            var result = ParseArgumentsJson(jsonFullPath);
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
                int optionIndex = Configurations.Count;
                var configuration = new ConfigurationFile(jsonPath, obj);
                Configurations.Add(configuration);
                
                // Recurse.
                if (obj.ContainsKey("configurationFile"))
                {
                    try
                    {
                        var jsonPaths = obj["configurationFile"].Values<string>().ToArray();
                        return TryParseArgumentsJsons(jsonPaths, configuration.DirectoryFullPath);
                    }
                    catch(Exception){}
                    try
                    {
                        var oneJsonPath = obj["configurationFile"].ToObject<string>();
                        return TryParseArgumentsJson(FileSystem.ToFullNormalizedPath(oneJsonPath, configuration.DirectoryFullPath));
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
        /// Only field types supported: string, int, bool, string[], List[string], int[], HashSet[string]
        /// </summary>
        public MappingResult FillObjectWithOptionValues(object objectToBeFilled)
        {
            // If the type of t is a struct, we need to box it, which is why we take an object here.
            MappingResult result = new MappingResult(objectToBeFilled);

            var type = objectToBeFilled.GetType();

            foreach (var (fieldInfo, optionAttribute) in IterateOptions(type))
            {
                // Paths must be strings
                Assert(!optionAttribute.IsPath 
                    || (fieldInfo.FieldType == typeof(string) 
                        || fieldInfo.FieldType == typeof(string[])
                        || fieldInfo.FieldType == typeof(HashSet<string>)));

                string name = fieldInfo.Name;

                // TODO: allow any case options??
                // Allow opOptionName for clarity
                // if (name.StartsWith("op"))
                //     name = name.Substring(2, name.Length - 2);
                
                bool hasOption = Options.TryGetValue(name, out var option);
                
                if (!hasOption)
                { 
                    JToken optionFromConfiguration = null;
                    int configurationIndex = -1;
                    for (int i = 0; i < Configurations.Count; i++)
                    {
                        if (Configurations[i].JsonRoot.ContainsKey(name))
                        {
                            optionFromConfiguration = Configurations[i].JsonRoot[name];
                            configurationIndex = i;
                            break;
                        }
                    }
                    
                    if (configurationIndex != -1)
                    {
                        // God I hate exceptions
                        try
                        {
                            object obj = optionFromConfiguration.ToObject(fieldInfo.FieldType);
                            if (optionAttribute.IsPath)
                            {
                                if (obj is IList<string> paths)
                                {
                                    for (int i = 0; i < paths.Count; i++)
                                        paths[i] = FileSystem.ToFullNormalizedPath(
                                            paths[i], Configurations[configurationIndex].DirectoryFullPath);
                                }
                                else if (obj is string path)
                                {
                                    obj = FileSystem.ToFullNormalizedPath(
                                        path, Configurations[configurationIndex].DirectoryFullPath);
                                }
                                else if (obj is HashSet<string> pathSet)
                                {
                                    obj = pathSet.Select(p => FileSystem.ToFullNormalizedPath(
                                        p, Configurations[configurationIndex].DirectoryFullPath)).ToHashSet();
                                }
                                else Assert(false);
                            }
                            
                            fieldInfo.SetValue(objectToBeFilled, obj);
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
                    fieldInfo.SetValue(objectToBeFilled, value);
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
                    || fieldInfo.FieldType == typeof(HashSet<string>)
                    || fieldInfo.FieldType.IsEnum);

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
                            $"{name}, indicated as flag, must be bool type.");

                        Debug.Assert(((bool) fieldInfo.GetValue(objectToBeFilled)) == false,
                            $"The default value for {name} flag must be true.");

                        if (hasOption)
                        {
                            option.IsMarked = true;
                            if (option.StringValue is null)
                            {
                                SetValue(true);
                            }
                            else if (!TrySetBoolValue())
                            {
                                result.Errors.Add($"Option {name} is a flag, you can only pass it a bool value.");
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
                    var strings = str.Split(',');
                    if (optionAttribute.IsPath)
                    {
                        for (int i = 0; i < strings.Length; i++)
                            strings[i] = FileSystem.ToFullNormalizedPath(strings[i]);
                    }
                    return strings;
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
                    string t = option.StringValue;
                    if (optionAttribute.IsPath)
                        t = FileSystem.ToFullNormalizedPath(t);
                    SetValue(t);
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
                    SetValue(new HashSet<string>(ParseAsStringArray()));
                }
                else if (fieldInfo.FieldType == typeof(int[]))
                {
                    var arr = ParseAsStringArray();
                    var ints = new int[arr.Length];
                    for (int i = 0; i < arr.Length; i++)
                        ints[i] = ParseAsInteger(arr[i]);
                    SetValue(ints);
                }
                else if (fieldInfo.FieldType.IsEnum)
                {
                    // int index = 0;
                    // bool good = true;
                    // // skip spaces
                    // while (option.StringValue.Length < index)
                    // {
                    //     if (char.IsWhiteSpace(option.StringValue[index]))
                    //     {
                    //         index++;
                    //         continue;
                    //     }
                    //     else if (!char.IsLetter(option.StringValue[index]))
                    //     {
                    //         result.Errors.Add($"Enumeration constants must ");
                    //         good = false;
                    //         break;
                    //     }
                    // }
                    if (!Enum.TryParse(fieldInfo.FieldType, option.StringValue, true, out object value))
                    {
                        result.Errors.Add($"The given enumeration constant {option.StringValue} is not valid.");
                    }
                    else
                    {
                        SetValue(value);
                    }
                }
            }

            return result;
        }

        private static readonly TableColumn[] _Header = new TableColumn[] 
        { 
            new TableColumn("Option").Centered(),
            new TableColumn("Type").Centered(),
            new TableColumn("Default/Config").Centered(),
            new TableColumn("Description").LeftAligned(),
        };
        private static readonly IRenderable[] _TableRowTemp = new IRenderable[4];


        public IEnumerable<(FieldInfo, OptionAttribute)> IterateOptions(System.Type type)
        {
            static FieldInfo[] GetFieldInfos(Type type)
            {
                return type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            }

            foreach (var fieldInfo in GetFieldInfos(type))
            foreach (var attr in fieldInfo.GetCustomAttributes(inherit: false))
            {
                if (!(attr is OptionAttribute optionAttribute))
                    continue;

                yield return (fieldInfo, optionAttribute);
            }
        }

        private string GetObjectHelpMessage(object command)
        {
            // No reason for it to be stored as a field.
            var helpMessageProperty = command.GetType().GetProperty("HelpMessage", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            if (helpMessageProperty is not null && helpMessageProperty.GetMethod is not null)
            {
                var message = helpMessageProperty.GetMethod.Invoke(command, null);
                if (message is string messageString)
                    return messageString;
            }
            return "";
        }

        private static void WriteOptionType(StringBuilder builder, FieldInfo fieldInfo, OptionAttribute optionAttribute, bool writeEnumAsString)
        {
            if (fieldInfo.FieldType.IsEnum)
            {
                // Could do an interface here, but for now just taking a string.
                if (writeEnumAsString)
                {
                    builder.Append("string");
                }
                else
                {
                    var items = new List<string>(); 
                    foreach (var name in Enum.GetNames(fieldInfo.FieldType))
                    {
                        if (fieldInfo.FieldType.GetField(name).GetCustomAttribute(typeof(HideOptionAttribute)) is null)
                        {
                            items.Add(name);
                        }
                    }
                    builder.Append("{");
                    builder.Append(string.Join(",\n", items));
                    builder.Append("}");
                }
            }
            // HashSet<string> or List<string>
            else if (fieldInfo.FieldType.GenericTypeArguments.Length > 0)
            {
                var n = fieldInfo.FieldType.Name;
                // This cuts off the ` and the number
                builder.Append(n.AsSpan(0, n.Length - 2));
                builder.Append("<"); 
                // TODO: We only allow strings here, so I'm not looping over it.
                if (optionAttribute.IsPath)
                {
                    builder.Append("Path");
                }
                else
                {
                    builder.Append(fieldInfo.FieldType.GenericTypeArguments[0].Name);
                }
                builder.Append(">");
            }
            // either a string or a string[]
            else if (optionAttribute.IsPath)
            {
                builder.Append("Path");
                if (fieldInfo.FieldType == typeof(string[]))
                    builder.Append("[]");
            }
            else
            {
                builder.Append(fieldInfo.FieldType.Name);
            }
        }


        /// <summary>
        /// Returns an even table filled with the information about options a given object takes.
        /// </summary>
        public Table GetHelpFor(object t)
        {
            var type = t.GetType();
            var table = new Table();
            table.AddColumns(_Header);
            table.Width = 140;

            foreach (var (fieldInfo, optionAttribute) in IterateOptions(type))
            {
                Debug.Assert(!string.IsNullOrEmpty(optionAttribute.Help),
                    "If it's super obvious what the option does, set the help to \" \"");

                var typeBuilder = new StringBuilder();
                WriteOptionType(typeBuilder, fieldInfo, optionAttribute, writeEnumAsString: false);                

                var otherThings = new List<string>();

                {
                    if (optionAttribute.IsRequired)
                        otherThings.Add("required");
                    // if (optionAttribute.IsRequiredForHelp)
                    //     AppendProperty("required for help");
                    if (optionAttribute.IsFlag)
                        otherThings.Add("flag");

                    if (otherThings.Count > 0)
                    {
                        typeBuilder.Append(" (");
                        typeBuilder.Append(string.Join(", ", otherThings));
                        typeBuilder.Append(")");
                    }
                }
                
                var defaultBuilder = new StringBuilder();

                // Required things cannot have default value, and flags are always false.
                if (otherThings.Count == 0)
                {
                    var value = fieldInfo.GetValue(t);

                    if (value is IEnumerable<string> arr)
                    {
                        defaultBuilder.Append("[");
                        defaultBuilder.Append(string.Join(",", arr));
                        defaultBuilder.Append("]");
                    }
                    else if (value is null)
                    {
                        defaultBuilder.Append("---");
                    }
                    else
                    {
                        defaultBuilder.Append(value);
                    }
                }


                // Option
                _TableRowTemp[0] = new Text(fieldInfo.Name);
                // Type
                _TableRowTemp[1] = new Text(typeBuilder.ToString());
                // Default
                _TableRowTemp[2] = new Text(defaultBuilder.ToString());
                // Description
                _TableRowTemp[3] = new Text(optionAttribute.Help);

                table.AddRow(_TableRowTemp);
                table.AddEmptyRow();
            }

            var style = new Style(Color.White, null, Decoration.Italic, 
                // Link is interesting. Could use this for documentation.
                link: null);
            table.Title = new TableTitle(GetObjectHelpMessage(t), style);

            return table;
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
            // -1 means it was passed via command line, hence the cwd is the origin directory
            public readonly int OriginConfigurationFileIndex;
            public readonly JProperty Property;

            public ConfigurationOption(int originConfigurationFileIndex, JProperty property)
            {
                OriginConfigurationFileIndex = originConfigurationFileIndex;
                Property = property;
            }
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
                    yield return new ConfigurationOption(i, property);
                }
                // It should only report the property in the first file it's found.
                configOptionsTemp.Add(property.Name);
            }
        }

        public string GetPropertyPathOfOption(in ConfigurationOption option) 
        {
            return $"File {Configurations[option.OriginConfigurationFileIndex].FileFullPath}, at {option.Property.Path}";
        }

        public string GetNukeJSONHelpFor(object t, CommandDescriptionInfo descriptionInfo)
        {
            #if false
            var listBuilder = new CodeListBuilder(CodeListBuilder.CommandSpaceSeparator);
            builder.Append("[");
            builder.IncreaseIndent();
            builder.NewLine();

            var type = t.GetType();
            StringBuilder typeBuilder = new();

            foreach (var (fieldInfo, optionAttribute) in IterateOptions(type))
            {
                listBuilder.MaybeAppendNewLineAndSeparatorOrSetHasWritten(ref builder);
                OptionInfo info;

                info.name = fieldInfo.Name;
             
                typeBuilder.Clear();
                WriteOptionType(typeBuilder, fieldInfo, optionAttribute, writeEnumAsString: true);
                info.type = typeBuilder.ToString();

                info.format = "-" + info.name + " " + "{value}";
                info.separator = ",";
                info.createOverload = null;
                info.help = optionAttribute.Help;

                info.WriteJsonObject(ref builder);
            }

            builder.DecreaseIndent();
            builder.NewLine();
            builder.Append("]");
            #endif

            var type = t.GetType();
            StringBuilder typeBuilder = new();
            var optionInfos = IterateOptions(type).Select(a => 
            {
                var (fieldInfo, optionAttribute) = a;

                OptionInfo info;

                info.name = fieldInfo.Name;
            
                typeBuilder.Clear();
                WriteOptionType(typeBuilder, fieldInfo, optionAttribute, writeEnumAsString: true);
                info.type = typeBuilder.ToString();

                info.format = "-" + info.name + " " + "{value}";
                info.separator = ",";
                info.createOverload = null;
                info.help = optionAttribute.Help;

                return info;
            }).ToArray();
            
            {
                var commandInfo = new CommandInfo
                {
                    help = GetObjectHelpMessage(t),
                    descriptionInfo = descriptionInfo,
                };
                Assert(descriptionInfo.taskNames.Length == 1, "Only a single task is supported right now.");
                var jobj = ParserHelpers.ConvertToJSONObject(commandInfo, optionInfos);
                return jobj.ToString(Formatting.Indented);
            }
        }
    }

    public struct OptionInfo
    {
        public string name;
        public string type;
        public string format;
        public string separator;
        public bool? createOverload;
        public string help;
    }

    public struct CommandDescriptionInfo
    {
        public string schema;
        public string[] references;
        public string name;
        public string officialUrl;
        public bool customExecutable;
        public string[] taskNames;
    }

    public struct CommandInfo
    {
        public string help;
        public CommandDescriptionInfo descriptionInfo;
    }

    public static class ParserHelpers
    {
        public static void LogHelpFor(this ArgumentParser parser, object obj)
        {
            var t = parser.GetHelpFor(obj);
            t.Expand();
            AnsiConsole.ResetColors();
            AnsiConsole.Write(t);
        }

        public static ArgumentParser DoSimpleParsing(object optionsObject, string[] args, NamedLogger logger, string configurationName = null)
        {
            var parser = new ArgumentParser();
            var result = parser.ParseArguments(args);
            if (result.IsError)
            {
                logger.LogError(result.Error);
                return parser;
            }

            if (configurationName is not null)
            {
                result = parser.MaybeParseConfiguration(configurationName);
                if (result.IsError)
                {
                    logger.LogError(result.Error);
                    return parser;
                }
            }

            if (parser.IsHelpSet)
            {
                parser.LogHelpFor(optionsObject);
                return parser;
            }
            var result2 = parser.FillObjectWithOptionValues(optionsObject);
            if (result2.IsError)
            {
                // TODO: enhance this check here, maybe
                if (parser.IsEmpty)
                {
                    parser.LogHelpFor(optionsObject);
                    return parser;
                }

                foreach (var error in result2.Errors)
                    logger.LogError(error);
                return parser;
            }

            var unrecognizedOptions = parser.GetUnrecognizedOptions();
            var unrecognizedOptionsFromConfiguration = parser.GetUnrecognizedOptionsFromConfigurations();

            if (unrecognizedOptions.Any() || unrecognizedOptionsFromConfiguration.Any())
            {
                parser.LogHelpFor(optionsObject);

                foreach (var opt in unrecognizedOptionsFromConfiguration)
                    logger.LogError($"Unrecognized configuration option {parser.GetPropertyPathOfOption(opt)}.");
                if (unrecognizedOptions.Any())
                    logger.LogError($"Unrecognized options: {String.Join(", ", unrecognizedOptions)}");
            }

            return parser;
        }


        public static void AppendJSONKeyValue(ref CodeBuilder builder, ref CodeListBuilder listBuilder, string key, string value)
        {
            listBuilder.MaybeAppendNewLineAndSeparatorOrSetHasWritten(ref builder);
            builder.Append("\"");
            builder.Append(key);
            builder.Append("\"");
            builder.Append(": ");
            builder.Append("\"");
            builder.Append(value);
            builder.Append("\"");
        }

        public static void WriteJsonObject(this in OptionInfo info, ref CodeBuilder builder)
        {
            builder.StartBlock();
            var listBuilder = new CodeListBuilder(CodeListBuilder.CommandSpaceSeparator);

            AppendJSONKeyValue(ref builder, ref listBuilder, nameof(info.name), info.name);
            AppendJSONKeyValue(ref builder, ref listBuilder, nameof(info.type), info.type);
            AppendJSONKeyValue(ref builder, ref listBuilder, nameof(info.format), info.format);
            if (info.separator is not null)
                AppendJSONKeyValue(ref builder, ref listBuilder, nameof(info.separator), info.separator);
            if (info.createOverload.HasValue)
                AppendJSONKeyValue(ref builder, ref listBuilder, nameof(info.createOverload), info.createOverload.Value.ToString());

            builder.EndBlock();

        }

        #if false
        public static void WriteJsonObject(this in CommandInfo info, ref CodeBuilder builder, IEnumerable<OptionInfo> optionInfos)
        {
            builder.StartBlock();
            var listBuilder = new CodeListBuilder(CodeListBuilder.CommandSpaceSeparator);

            AppendJSONKeyValue(ref builder, ref listBuilder, "$schema", info.desschema);
            AppendJSONKeyValue(ref builder, ref listBuilder, nameof(info.references), info.schema);

            builder.EndBlock();
        }
        #endif


        public static JObject ConvertToJSONObject(this in OptionInfo info)
        {
            JObject jobj = new();

            jobj[nameof(info.name)] = info.name;
            jobj[nameof(info.type)] = info.type;
            jobj[nameof(info.format)] = info.format;
            if (info.separator is not null)
                jobj[nameof(info.separator)] = ",";
            if (info.createOverload.HasValue)
                jobj[nameof(info.createOverload)] = info.createOverload.Value;

            return jobj;
        }

        public static JObject ConvertToJSONObject(this in CommandInfo info, IEnumerable<OptionInfo>[] optionInfos)
        {
            JObject jobj = new();

            ref readonly var descInfo = ref info.descriptionInfo;

            jobj["$schema"] = descInfo.schema;
            jobj[nameof(descInfo.name)] = descInfo.name;
            jobj[nameof(descInfo.officialUrl)] = descInfo.officialUrl;
            jobj[nameof(descInfo.references)] = new JArray(descInfo.references);
            jobj[nameof(descInfo.customExecutable)] = descInfo.customExecutable;
            jobj[nameof(descInfo.name)] = descInfo.name;
            jobj[nameof(info.help)] = info.help;

            {
                var tasks = new JArray();
                for (int i = 0; i < descInfo.taskNames.Length; i++)
                {
                    var taskOuterObj = new JObject();
                    var taskObjInner = new JObject();
                    var properties = new JArray();
                    
                    var taskName = descInfo.taskNames[i];
                    taskOuterObj[taskName] = taskObjInner;
                    taskObjInner["properties"] = properties;

                    foreach (var optionInfo in optionInfos[i])
                        properties.Add(ConvertToJSONObject(in optionInfo));
                    
                    tasks.Add(taskOuterObj);
                }
                jobj["tasks"] = tasks;
            }

            return jobj;
        }

        public static JObject ConvertToJSONObject(this in CommandInfo info, IEnumerable<OptionInfo> optionInfos)
        {
            return ConvertToJSONObject(info, new[]{ optionInfos });
        }
    }
}