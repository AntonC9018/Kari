# Kari, the code generator

This project started as the code generator for [the main project it was used in](https://github.com/PunkyIANG/a-particular-project), but eventually became pretty much a standalone tool, easily extensible via plugins and very flexible. 

Kari is a Roslyn based tool. 
It can read the source code of your C# project, analyze it, and generate new type-safe code based on it.
This tool can often replace slow or annoying to write code based on reflection, boilerplate, or IL-emission; encourages declarative programming.

The generated code comes exclusively from plugins, so you can adjust the behavior of Kari by writing your own domain specific plugins.

**Currently done:**

- Proper custom argument parsing.
- Dynamic plugin loading.
- Understading of Unity asmdef's.
- Generating into a central/nested directory/file, for you to choose.
- Plugin automatic installation via packages.config.

**Kind of done**:

- Installation of Kari as a standalone dotnet tool so that it can be used without cloning this repo.
- A friendly way to define plugins. You can use the Kari.Plugin.Sdk, which does most configuration needed for a plugin to function.
- Dotnet new templates for getting started with plugins.

**Planned:**

- **Publish to nuget.** Currently, I'm only using it locally.
- Code generation for conventional csproj structured projects is currently disregarded (currently replaced with the directory-based method). Solution-based code generation should also be added.
- Tests. Tests are kind of difficult when it comes to code generation, but it must be done at some point. I have already started on internal plugin testing.
- Make a proper \[仮\] logo.


## How to use

- Install Kari (currently must be installed from source, see [Building](#building) below).
- Install plugins you want to use. Either put them in a single folder, or make use of Nuget's "packages.config" to fetch them automatically.
- Call Kari to generate code for your project, passing in either the path to the plugin folder, or the path to the "packages.config".  
- (Optionally) Create a configuration file "kari.json" in the directory with your project in order to configure once and keep reusing the configuration (in fact can be put anywhere, see the [Options](#options) section below to learn about configuration files).


## Building

### Prerequisites

You must have [.NET 6](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) (it also comes with Visual Studio) installed to be able to use Kari at all.
The runtime version of the project you will be using Kari on does not matter though.

To make your life easier, I have made a couple of helper D scripts, so [install D](https://dlang.org/download.html) to be able to use those too.


### Build the projects

`dotnet tool restore` followed by `dotnet build` should build all projects in the solution.

`Kari.Generator.exe` can be run on its own. 
It's located in `build_folder\bin\Kari.Generator\Debug\net8.0\Kari.Generator.exe`. 


### Helper D scripts

In order to install it as a tool, use the `install_kari_globally` D script.
```
cd debug_helpers
dub --config=install_kari_globally -- --repackage --configuration=Debug
```

Which allows you to call kari like this:
```
kari
```

The installation option via Nuget (simply `dotnet tool install`) will be available in the near future, but you can in fact already use it with local tool manifests. 
For that you'll need to make Nuget able to find the packages output by kari.
For this exact purpose, use the `build_nuget` D script, passing `--createFeed`, which will take care of all that for you.

```
cd debug_helpers
dub --config=build_nuget -- --repackage --createFeed
```


## Options

Kari is not designed for single-time use and is mostly for use in build scripts, which is why the argument parser is very strict:

- The arguments must be capitalized exactly like in the help message;
- There are no positional arguments, to reduce confusion for people reading the script;
- There are no shortened versions for arguments (I personally despise these, especially their use in scripts by Linux people);
- If you pass an unrecognized option, you ALWAYS get an error. Sure, this may break backwards compatibility, but I think consistency is more worth it.

The arguments are passed by *single-dashes*, double dashes are not supported! 
Separate the arguments by commas if an argument is an array or a hash set. For example:

```
# Valid syntax
kari -option Value

# Double dash, not allowed
kari --help

# Passing a list
kari -pluginPaths "pluginsFolder,C:/absolute/path/plugins,build_folder/compiledPlugin.dll"
```

Options may be set via a json configuration file.
Kari automatically searches for a `kari.json` file in the working directory and next to the executable.
Additionally, paths to any number of configuration files may be passed on the command line:
```
kari -configurationFile "file1.json,C:/file2.json"
```

Arguments passed on the command line take precedence over ones imported from configuration files.
Also, configuration files specified first have precedence over the ones specified after.
Configuration files may include other configuration files, by specifying a property "configurationFile", which can either be an array of strings or a string with the relative or absolute path to another configuration file.
The configuration files are searched for relative to the folder of the configuration file they were included from, if a relative path is provided.

Calling Kari without any arguments (in a folder without kari.json) gives the following help message:

```
$ .\build_folder\bin\Kari.Generator\Debug\net8.0\Kari.Generator.exe
                                                Use Kari to generate code for a C# project.
┌───────────────────────────────────┬───────────────────┬─────────────────┬────────────────────────────────────────────────────────────────┐
│              Option               │       Type        │ Default/Config  │ Description                                                    │
├───────────────────────────────────┼───────────────────┼─────────────────┼────────────────────────────────────────────────────────────────┤
│            inputFolder            │       Path        │        .        │ Input path to the directory containing source files or         │
│                                   │                   │                 │ projects.                                                      │
│                                   │                   │                 │                                                                │
│            pluginPaths            │      Path[]       │       ---       │ Plugins folder or paths to individual plugin dlls.             │
│                                   │                   │                 │                                                                │
│       pluginConfigFilePath        │       Path        │       ---       │ Path to `packages.config` that you're using to manage          │
│                                   │                   │                 │ packages. The plugins mentioned in that file will be imported. │
│                                   │                   │                 │                                                                │
│     generatedNamespaceSuffix      │      String       │    Generated    │ The suffix added to the project namespace to generate the      │
│                                   │                   │                 │ output namespace.                                              │
│                                   │                   │                 │                                                                │
│        conditionalSymbols         │     String[]      │       ---       │ Conditional compiler symbols. Ignored if a project file is     │
│                                   │                   │                 │ specified for input. (Currently ignored)                       │
│                                   │                   │                 │                                                                │
│           rootNamespace           │      String       │                 │ Set input namespace root name.                                 │
│                                   │                   │                 │                                                                │
│            pluginNames            │     String[]      │       ---       │ Plugin names to be used for code analysis and generation. All  │
│                                   │                   │                 │ plugins are used by default.                                   │
│                                   │                   │                 │                                                                │
│           generatedName           │      String       │    Generated    │ The code by default will be generated in a nested folder with  │
│                                   │                   │                 │ this name. If `centralInput` is true, this indicates the       │
│                                   │                   │                 │ central output folder path, relative to `input`. If            │
│                                   │                   │                 │ `singleFileOutput` is set to true, '.cs' may be appended to    │
│                                   │                   │                 │ this name to indicate the output file name.                    │
│                                   │                   │                 │                                                                │
│            outputMode             │ {NestedDirectory, │ NestedDirectory │ Where to place the generated files.                            │
│                                   │ CentralDirectory, │                 │                                                                │
│                                   │    NestedFile,    │                 │                                                                │
│                                   │   CentralFile}    │                 │                                                                │
│                                   │                   │                 │                                                                │
│             inputMode             │   {Autodetect,    │   Autodetect    │ `UnityAsmdefs` means it will search for asmdefs.               │
│                                   │     MSBuild,      │                 │ `MSBuild` and `ByDirectory` are equivalent: they assume the    │
│                                   │   UnityAsmdefs,   │                 │ given path is a root folder, where each subfolder is a         │
│                                   │    Monolithic,    │                 │ separate project. Nested projects are currently not allowed.   │
│                                   │   ByDirectory}    │                 │ `Monolithic` there are source files to be analysed in the root │
│                                   │                   │                 │ folder, as well as in nested folders.                          │
│                                   │                   │                 │ `Autodetect` means that the input will be selected by looking  │
│                                   │                   │                 │ at the file system's entries. If there are asmdefs, Unity will │
│                                   │                   │                 │ be guessed, if there are source files in root, Monolithic. At  │
│                                   │                   │                 │ last, it will default to `ByDirectory` if none of the above    │
│                                   │                   │                 │ were true.                                                     │
│                                   │                   │                 │                                                                │
│         commonProjectName         │      String       │                 │ The name of the common project. Corresponds either to the      │
│                                   │                   │                 │ directory name (`ByDirectory`) or the project file name        │
│                                   │                   │                 │ (`Unity`). Leave at default to let it autodetect.              │
│                                   │                   │                 │                                                                │
│          rootProjectName          │      String       │                 │ The name of the root project. Corresponds either to the        │
│                                   │                   │                 │ directory name (`ByDirectory`) or the project file name        │
│                                   │                   │                 │ (`Unity`). By default, it would generate in the root           │
│                                   │                   │                 │ directory. You can either pass a name of one of the projects   │
│                                   │                   │                 │ that you want to be root here, or just give it the folder name │
│                                   │                   │                 │ relative to the root directory and it would generate into that │
│                                   │                   │                 │ folder.                                                        │
│                                   │                   │                 │                                                                │
│           ignoredNames            │   List<String>    │    [obj,bin]    │ The directories, source files in which will be ignored. The    │
│                                   │                   │                 │ generated source files are always ignored.                     │
│                                   │                   │                 │                                                                │
│         ignoredFullPaths          │   List<String>    │       []        │ The full directory or file paths which will be ignored when    │
│                                   │                   │                 │ reading source files. The generated source files are always    │
│                                   │                   │                 │ ignored.                                                       │
│                                   │                   │                 │                                                                │
│ whitelistGeneratedCodeForProjects │  HashSet<String>  │       ---       │ Which projects to generate code for. (Unimplemented)           │
│                                   │                   │                 │                                                                │
│     whitelistAnalyzedProjects     │  HashSet<String>  │       ---       │ Which projects to read the code of. (Unimplemented)            │
│                                   │                   │                 │                                                                │
│ additionalAnnotationAssemblyPaths │      Path[]       │       ---       │ Paths to assemblies to load annotations from. The `object`     │
│                                   │                   │                 │ assembly is always loaded. (Unimplemented)                     │
│                                   │                   │                 │                                                                │
│ additionalAnnotationAssemblyNames │     String[]      │       ---       │ Names of assemblies to load annotations from. These will be    │
│                                   │                   │                 │ searched in the default location. Use                          │
│                                   │                   │                 │ `additionalAnnotationAssemblyPaths` to straight up load from   │
│                                   │                   │                 │ paths. (Unimplemented)                                         │
│                                   │                   │                 │                                                                │
└───────────────────────────────────┴───────────────────┴─────────────────┴────────────────────────────────────────────────────────────────┘
```

To see help from plugins, you need to pass the location of the plugin (any path to it) as an argument.

```
$ .\build_folder\bin\Kari.Generator\Debug\net8.0\Kari.Generator.exe -pluginPaths "build_folder\bin\Terminal\Debug\net8.0\Kari.Plugins.Terminal.dll" -help

...

Showing help for `Kari.Plugins.Terminal.TerminalAdministrator`.

┌──────────────────────────────┬─────────────┬────────────────────────────┬────────────────────────────────────────────────────────────────┐
│            Option            │    Type     │       Default/Config       │ Description                                                    │
├──────────────────────────────┼─────────────┼────────────────────────────┼────────────────────────────────────────────────────────────────┤
│       terminalProject        │   String    │          Terminal          │ Namespace of the Terminal project.                             │
│                              │             │                            │                                                                │
└──────────────────────────────┴─────────────┴────────────────────────────┴────────────────────────────────────────────────────────────────┘
```


## Plugins

Plugins are assemblies that get to analyze the user code provided by Kari, and generate the output code.
Kari links to plugins dynamically, thus it's not coupled with any particular code generation logic.

Kari groups asmdef projects by their folder, and generates its output in a "Generated" subfolder next to each asmdef project by default (can be configured via the `-outputMode` option). 

Kari also generates root output, such as startup functions. This runner code may reference any of the other assemblies and no assembly can reference it back.
Kari assumes a second special "Common" project, which defaults to the root project when specified no value. You should set it via the `-commonProjectName` option. This project would contain the most agnostic code and must not depend on any of the other projects.


### Administrators

Plugins must define an administrator class that manages code generation provided by the plugin. A single plugin may define multiple administrators.

Administrators must be public classes implementing `IAdministrator`. They must implement at least the following methods:

1. `void Initialize()`, used to initialize any global state, and use global state to initialize oneself. This function is called before any code is analyzed, but after the projects have been found.
2. `Task Collect()`, used to collect any symbols needed for code generation. The symbols are usually put in a list, often wrapped in custom `Info` classes. 
3. `Task Generate()`, used to write output files.
4. `string GetAnnotations()`, used to return the provided to the consumer code interface (the attributes etc), as a string.

`AdministratorHelpers` provides helper functions of managing a group of per-project (asmdef) analyzers, which may `Collect()` the symbols required to those, per-project. See the Flags plugin for a simple example.


### How to make a plugin

The easiest way to get started is to use a starter template. 
To generate a starter boilerplate for your plugin and add it to the solution, first install the templates.

If it does not work properly (beats me why exactly, but it would often get confused by the lack of proper versioning and just restore previous builds), delete the `build_folder\nuget_sources\kari.plugin.templates` folder completely and let it rebuild. 
Or just force-rebuild everything with `dub --config=build_plugin -- --repackage --clearAll`.

```
dotnet pack --configuration Release source\dotnet_new_templates\Templates.csproj
dotnet new --install Kari.Plugin.Templates
```

Then instantiate one of them, e.g.:
```
dotnet new kari_basic_plugin_project --name PluginName
```
This generates 5 files: the csproj file, *Administrator* class, *Analyzer* class, a file with *Annotations* and a helper file generated from those Annotations.
Out of these parts, only an *Administrator* (and a csproj file obviously) is absolutely required from a plugin, but the other parts are also common.

Check out the comments in the different files to understand how it comes together.

> I have not tested the other templates properly, which I won't do before the first normal release.
> So there's no guarantee the other templates would work.


### Attribute helpers

`Kari.Annotator` makes it easy for you to define attributes and query them in your code. 
All you need to do, is to make a file whose name ends with `Annotations.cs`, including simply `Annotations.cs` and put all of your attributes in there. At build time, a generated file will be created, containing a class `DummyAttributes` with the source text of your annotations, and a `Symbols` class, containing the singleton that defines attribute wrappers for your classes for querying them in customer code.

To query symbols using these attributes, use `TryGetAttribute()` extension method of `ISymbol`:
```C#
// The logger used to output errors, e.g. invalid syntax. 
var logger = new Logger("MyLogger");
// TryGetAttribute returns true if the symbol contained the given attribute
if (symbol.TryGetMyAttribute(compilation, out MyAttribute attribute)
{
    // attribute contains an instance of your attribute, just like you would have in the customer code.
}
```

It supports any kinds of array, and maps `System.Type` into type symbols.
The annotator will replace `ITypeSymbol` and `INamedTypeSymbol` with `System.Type` in the client code.


### Getting command line options

You can mark any fields in your administrator with `[Option]` to associate to them the options passed on the command line with exactly the same name.
You can make them required, by setting `IsRequired` to true in the constructor.
See the file `ArgumentParsing.cs` for an API overview, and override `IAdministrator.GetArgumentObject()` if you want to define the options somewhere else than directly in the administrator class. 
See an example argument class in Kari.Generator's sources

You can define a `HelpMessage` static or instance getter property, which will be used when your plugin is prompted for help with the help flag.


## Internal plugins

None of the plugins are included by default by Kari, however, there are some useful ones already defined in this repo.
They all have Readme's that you can check out.

### Flags

Generates useful functions for flag enums.

### Terminal

Generates backend code for terminal commands. See a complete example repo [here](https://github.com/AntonC9018/command_terminal).

### UnityHelpers

Generates boilerplate helper functions related to Unity. 
Currently, it makes helper functions for changing individual coordinates of `Vector`'s.

The idea comes from [here](https://github.com/TobiasWehrum/unity-utilities/blob/c78da2928b1f7b73046a697185271e7effeddd1f/UnityHelper/UnityHelper.cs#L199), but results in no runtime penalty of checking nullable types.

### DataObject

Automatically defines `==`, `!=`, `Equals()` and some others for conceptually data-only objects (like records in C# 9). 


## Pitfalls

Currently, only DataObject plugin has a readme.

Currently, only DataObject plugin has a test.

Currently, the other plugins besides DataObject have not been tested on the latest version of Kari, but I see no reason why they would fail.

Currently, all the plugins assume all types are not nested within other types. 
If you try to force generating over a nested type, the generated code will likely not compile.
