module extract_plugin;

import common;
import std.file;
import std.path;
import std.process;

// I don't recommend you use this script, Kari now supports nuget as the package manager
// via packages.config.

struct Options
{
    @("Comma separated names of plugins to install, paths to plugin archives, ~~or folders with archives~~.")
    string[] pluginName;

    @("The installation folder where the plugins will be put.")
    string pluginFolder = null;//"kari_plugins";

    @("It will try and get the plugin folder from this file, if you didn't give it one. Does load config files recursively.")
    string kariConfigurationFile = null; // "kari.json";

    @("If not specified, the plugins will be installed in the default nuget location.")
    string packageSourceFolder = null;

    // @("Whether instead of installing the plugins from the registry, it should unpack local files.")
    // bool treatNamesAsPathsAndUnpack = false;

    @("Pass this to use the globally installed `nuget.exe`.")
    bool nugetUseGlobalAlias = false;

    @("Nuget installation directory path.")
    string nugetDirectoryPath = defaultNugetExecutableDirectoryPath;
}

void main(string[] args)
{
    Options op;
    auto helpInfo = getOptions(args, op);
    if (helpInfo.helpWanted || op.pluginName.length == 0)
    {
        defaultGetoptPrinter("", helpInfo.options);
        return;
    }

    // string[] pluginNames = op.pluginName.split(",");
    string[] pluginNames = op.pluginName;
    string nugetExecutablePath = "nuget";
    string nugetInstallDirectory;
    bool willNeedNuget = !pluginNames.all!(p => isPathNugetPackage(p) || exists(p));
    
    if (willNeedNuget)
    {
        if (!op.nugetUseGlobalAlias)
        {
            if (!exists(op.nugetDirectoryPath))
                mkdirRecurse(op.nugetDirectoryPath);
            nugetExecutablePath = op.nugetDirectoryPath.buildPath("nuget.exe");
            initNuget(nugetExecutablePath);
        }
        nugetInstallDirectory = op.packageSourceFolder 
            ? op.packageSourceFolder 
            : getNugetList(nugetExecutablePath)["global-packages"];
    }
    

    op.pluginFolder = {

        auto makeAndReturn(string folder)
        {
            if (!exists(folder))
                mkdirRecurse(folder);
            return folder;
        }

        if (op.pluginFolder)
            return makeAndReturn(op.pluginFolder);

        if (!op.kariConfigurationFile)
        {
            if (!exists(defaultKariConfigFileName))
                return makeAndReturn(defaultKariPluginsDirectoryName);
            op.kariConfigurationFile = defaultKariConfigFileName;
        }

        assert(exists(op.kariConfigurationFile), "The provided configuration file does not exist.");

        import std.json;

        string[] visitedFiles;
        string maybeFindPluginDirectoryRecursively(string kariConfigFilePath)
        {
            if (visitedFiles.canFind(kariConfigFilePath))
                return null;
            visitedFiles ~= kariConfigFilePath;
            if (!exists(kariConfigFilePath))
                return null;

            JSONValue kariConfiguration = parseJSON(std.file.readText(kariConfigFilePath));
            if (auto pluginPaths = "pluginPaths" in kariConfiguration)
            {
                auto splitPaths = pluginPaths.array.map!(a => a.str);
                foreach (p; splitPaths)
                {
                    if (exists(p) && DirEntry(p).isDir)
                        return p;
                }
            }
            if (auto configFiles = "configurationFile" in kariConfiguration)
            {
                if (configFiles.type == JSONType.string)
                    return maybeFindPluginDirectoryRecursively(configFiles.str);
                
                foreach (file; configFiles.array.map!(a => a.str))
                {
                    if (auto t = maybeFindPluginDirectoryRecursively(file)) 
                        return t;
                }
            }
            return null;
        }
        if (auto f = maybeFindPluginDirectoryRecursively(op.kariConfigurationFile))
            return f;
        return makeAndReturn(defaultKariPluginsDirectoryName);
    }();

    writeln("The plugin folder is: ", op.pluginFolder);


    foreach (pluginId; pluginNames)
    {
        string pluginZipPath;
        import std.string : toLower;
        if (isPathNugetPackage(pluginId))
        {
            assert(exists(pluginId));
            pluginZipPath = pluginId;
        }
        else if (exists(pluginId))
        {
            assert(0, "Folders are unimplemented");
            // auto args1 = [nugetExecutablePath, "add", pluginId];
            // if (op.packageSourceFolder)
            //     args1 ~= ["-Source", op.packageSourceFolder];
            
            // auto res = execute(args1);
        }
        else 
        {
            auto args1 = [nugetExecutablePath, "install", pluginId];

            if (op.packageSourceFolder)
                args1 ~= ["-Source", op.packageSourceFolder];

            auto res = execute(args1);
            assert(res.status == 0);
            writeln(res.output);

            pluginZipPath = nugetInstallDirectory
                .buildPath(pluginId.toLower)
                .getEntryWithLatestChange
                .dirEntries(SpanMode.shallow)
                .find!isPathNugetPackage
                .front;
        }
        
        import std.zip;
        auto archive = new ZipArchive(std.file.read(pluginZipPath));
        bool wrote = false;
        foreach (key, member; archive.directory)
        {
            import std.string : startsWith;
            if (!key.startsWith("lib/"))
                continue;
            const dllName = key.baseName;
            if (dllName.extension != ".dll")
                continue;
            archive.expand(member);
            auto path = op.pluginFolder.buildPath(dllName);
            std.file.write(path, member.expandedData);
            wrote = true;
        }
        assert(wrote, "The plugin din not have the lib folder, or did not have any dlls in the lib folder.");
    }
}