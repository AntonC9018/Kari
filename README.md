# Kari, the code generator

This project started as the code generator tightly coupled with [the main project it was used in](https://github.com/PunkyIANG/a-particular-project), but eventually became pretty much a standalone tool, easily extensible via plugins and very flexible. 

Kari is a Roslyn based tool. 
It can read the source code of your C# project, analyze it, and generate new type-safe code based on it.
This tool can often replace slow or annoying to write code based on reflection, boilerplate, or IL-emission; encourages declarative programming.

**Currently done:**

- Proper custom argument parsing;
- Dynamic plugin loading;
- Understading of Unity asmdef's (although each file must also explicitly specify the namespace of the asmdef it's in).


**Planned:**

- Installation of Kari as a standalone dotnet tool so that it can be used without cloning this repo;
- A friendlier way to define plugins. Most things that plugins need are defined in `Directory.Build.props`, and in the `Plugin.props`, but it's not convenient to import from anywhere else but Kari.Plugins at the moment.
- Decouple Baton (the python CLI from the main project) from Kari a bit further. The most prominent thing to refactor is the plugin template generation, which should be in Kari itself.
- Code generation for conventional csproj structured projects is probably broken. Solution-based code generation should also be added.
- Tests. Tests are kind of difficult when it comes to code generation, but it must be done at some point.
- Make a proper \[仮\] logo.


## Options

Kari is not designed for single-time use and is mostly for use in build scripts, which is why the argument parser is very strict:

- The arguments must be capitalized exactly like in the help message;
- There are no positional arguments, to reduce confusion for people reading the script;
- There are no shortened versions for arguments (I personally despise these, especially their use in scripts by Linux people);
- If you pass an unrecognized option, you ALWAYS get an error. Sure, this may break backwards compatibility, but I don't really care.

The arguments are passed by *single-dashes*, double dashes are not supported! 
Separate the arguments by commas if an argument is an array or a hash set. For example:

```
# Valid syntax
kari -option Value 

# Double dash, not allowed
kari --help

# Passing a list
kari -pluginsLocations "pluginsFolder,C:/absolute/path/plugins,Build/compiledPlugin.dll"
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

> Currently, the configuration file is searched for relative to the cwd.

Calling Kari without any arguments gives the following help message:

```
$ Build\bin\Kari.Generator\Debug\netcoreapp3.1\kari.exe
Use Kari to generate code for a C# project.

Option                       Type                          Description
---------------------------------------------------------------------------------------------------------------------------
input                        String (required)             Input path to MSBuild project file or to the directory containin
                                                           g source files.
pluginsLocations             String[] (required)           Plugins folder or paths to individual plugin dlls.
generatedName                String = Generated            The suffix added to each subproject (or the root project) indica
                                                           ting the output folder.
conditionalSymbols           String[] =                    Conditional compiler symbols. Ignored if a project file is speci
                                                           fied for input.
rootNamespace                String =                      Set input namespace root name.
clearOutput                  Boolean (flag)                Delete all cs files in the output folder.
pluginNames                  String[] =                    Plugin names to be used for code analysis and generation. All pl
                                                           ugins are used by default.
singleFileOutput             Boolean (flag)                Whether to output all code into a single file.
monolithicProject            Boolean (flag)                Whether to not scan for subprojects and always treat the entire
                                                           codebase as a single root project. This implies the files will b
                                                           e generated in a single folder. With `singleFileOutput` set to t
                                                           rue implies generating all code for the entire project in the si
                                                           ngle file.
commonNamespace              String = $Root.Common         The common project namespace name (use $Root to mean the root na
                                                           mespace). This is the project where all the attributes and other
                                                            things common to all projects will end up. Ignored when `monoli
                                                           thicProject` is set to true.
independentNamespaceParts    HashSet`1 = [Editor,Tests]    The subnamespaces ignored for the particular project, but which
                                                           are treated as a separate project, even if they sit in the same
                                                           root namespace.
treatEditorAsSubproject      Boolean = True                Whether to treat 'Editor' folders as separate subprojects, even
                                                           if they contain no asmdef. Only the editor folder that is at roo
                                                           t of a folder with asmdef is regarded this way, nested Editor fo
                                                           lders are ignored.
```

To see help from plugins, you need to pass the location of the plugin (any path to it) as an argument.

```
$ Build\bin\Kari.Generator\Debug\netcoreapp3.1\kari.exe -input "." -pluginsLocations "Build\bin\Terminal\Debug\netcoreapp3.1\Kari.Plugins.Terminal.dll" -help

...

Showing help for `Kari.Plugins.Terminal.TerminalAdministrator`.
Option             Type                 Description
--------------------------------------------------------------------------
terminalProject    String = Terminal    Namespace of the Terminal project.
```


## More info

See `source/README.md` for more info about the code generator.


## Building

`dotnet build` should build all projects in the solution. It will fail the first time, because the generated files are not added in the compilation, but it will work the second time.

`kari.exe` can be run on its own. 
It's located in `Build\bin\Kari.Generator\Debug\netcoreapp3.1\kari.exe`. 
I'm going to add a more friendly way of calling it though.

In order to install it as a tool, currently do:

```
dotnet pack --configuration Release
dotnet tool update --global Kari.Generator --add-source Build\bin\Kari.Generator\Release
```

Which allows you to call kari like this:
```
kari
```

But I'm currently figuring out a simpler way of publishing and installing it.