**Kari** is the temporary name of the code generator.

## How to use?

Kari has been designed for use in [this particular project](https://github.com/PunkyIANG/a-particular-project), and has not been tested anywhere else (yet).

The further steps (baton) assumes being in scope of that project.

You can call `baton kari unity` to run the code generator on the subproject, but the code generator can be used directly, without Baton.
You can use e.g. `dotnet run -p Kari.Generator/Kari.Generator.csproj` to compile and run Kari.
For further help, call Kari without arguments to see the different options.

Baton also provides commands for compiling Kari and the plugins: do `baton kari build --help` for more info


## Plugins

Plugins are assemblies that get to analyze the user code provided by Kari, and generate the output code.
Kari links to plugins dynamically, thus it's not coupled with any particular code generation logic.

Kari groups asmdef projects by their namespace, and generates its output in a subfolder next to each asmdef project ("Generated" by default). 
Kari also generates root output, such as startup functions. This runner code may reference any of the other assemblies and no assembly can reference it back.

### Administrators

Plugins must define an administrator class that manages code generation provided by the plugin. A single plugin may define multiple administrators.

Administrators must be public classes implementing `IAdministrator`. They must implement at least the following methods:

1. `void Initialize()`, used to initialize any global state, and use global state to initialize oneself. This function is called before any code is analyzed, but after the projects have been found.
2. `Task Collect()`, used to collect any symbols needed for code generation. The symbols are usually put in a list, often wrapped in custom `Info` classes. 
3. `Task Generate()`, used to write output files.
4. `string GetAnnotations()`, used to return the provided to the consumer code interface (the attributes etc), as a string.

`MasterAnalyzer` class provides helper functions of managing a group of per-project (asmdef) analyzers, which may `Collect()` the symbols required to those, per-project. See the Flags plugin for a simple example.


### How to make a plugin

The easiest way to get started is to use a starter template. 
To generate a starter boilerplate for your plugin and add it to the solution, use `baton kari new_plugin -name NAME_OF_PLUGIN`.
This generates 5 files: the csproj file, *Administrator* class, *Analyzer* class, a file with *Annotations* and a T4 template for code generation.
Out of these parts, only an *Administrator* (and a csproj file obviously) is absolutely required from a plugin, but the other parts are also common.

The key part is to import the Plugin properties in the csproj file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Must match the namespace -->
    <AssemblyName>Kari.Plugins.Flags</AssemblyName>
  </PropertyGroup>

  <ImportGroup>
    <!-- KariPluginPropsPath gets the absolute path to Plugin.props -->
    <!-- "..\Plugin.props" should also work -->
    <Import Project="$(KariPluginPropsPath)" />
  </ImportGroup>
</Project>
```


### Attribute helpers

`Plugin.props` makes it easy for you to define attributes and query them in your code. 
All you need to do, is to make a file whose name ends with `Annotations.cs`, including simply `Annotations.cs` and put all of your attributes in there. At build time, a generated file will be created, containing a class `DummyAttributes` with the source text of your annotations, and a `Symbols` class, containing the singleton that defines attribute wrappers for your classes for querying them in customer code.

To query symbols using these attributes, use `TryGetAttribute()` extension method of `ISymbol`:
```C#
// The logger used to output errors, e.g. invalid syntax. 
var logger = new Logger("MyLogger");
// TryGetAttribute returns true if the symbol contained the given attribute
if (symbol.TryGetAttribute(Symbols.MyAttribute, logger, out MyAttribute attribute)
{
    // attribute contains an instance of your attribute, just like you would have in the customer code.
}
```

Currently, it does not support arrays other than string arrays, but I have not yet had a use case for that.


### Getting command line options

You can mark any fields in your administrator with `[Option]` to associate options passed via command line with exactly the same name to them.
You can make them required, by setting `IsRequired` to true in the constructor.
See the file `ArgumentParsing.cs` for an API overview, and override `IAdministrator.GetArgumentObject()` if you want to define the options somewhere else than directly in the administrator class.

You can define a `HelpMessage` static or instance getter property, which will be used when your plugin is prompted for help with the help flag.

## Default plugins

None of the plugins are included by default by Kari, however, there are some useful ones already defined in this repo.

### Flags

Generates useful functions for flag enums.

### Terminal

Generates backend code for terminal commands. See a complete example repo [here](https://github.com/AntonC9018/command_terminal).

### UnityHelpers

Generates boilerplate helper functions related to unity. Currently, makes helper functions for changing individual coordinates of `Vector`'s.

The idea comes from [here](https://github.com/TobiasWehrum/unity-utilities/blob/c78da2928b1f7b73046a697185271e7effeddd1f/UnityHelper/UnityHelper.cs#L199), but results in no runtime penalty of checking nullable types.

### DataObject

Automatically defines `==`, `!=`, `Equals()` and some others for conceptually data-only objects. 
