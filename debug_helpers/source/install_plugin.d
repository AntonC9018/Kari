module extract_plugin;

import common.util;
import common.nuget;
import std.file;
import std.path;
import std.process;

struct Options
{
    @("Comma separated names of plugins to install, paths to plugin archives, or folders with archives.")
    string[] pluginName;

    @("The installation folder where the plugins will be put.")
    string pluginFolder = "kari_plugins";

    @("If not specified, the plugins will be installed in the default nuget location.")
    string packageSourceFolder = null;

    @("Whether instead of installing the plugins from the registry, it should unpack local files.")
    bool treatNamesAsPathsAndUnpack = false;

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

    if (!op.nugetUseGlobalAlias)
    {
        if (!exists(op.nugetDirectoryPath))
            mkdirRecurse(op.nugetDirectoryPath);
        nugetExecutablePath = op.nugetDirectoryPath.buildPath("nuget.exe");
        initNuget(nugetExecutablePath);
    }

    if (!exists(op.pluginFolder))
        mkdirRecurse(op.pluginFolder);
    string nugetInstallDirectory = op.packageSourceFolder 
        ? op.packageSourceFolder 
        : getNugetList(nugetExecutablePath)["global-packages"];

    foreach (pluginId; pluginNames)
    {
        string pluginZipPath;
        import std.string : toLower;
        if (pluginId.extension.toLower == ".nupkg")
        {
            assert(exists(pluginId));
            pluginZipPath = pluginId;
            
            // auto args1 = [nugetExecutablePath, "add", pluginId];
            // if (op.packageSourceFolder)
            //     args1 ~= ["-Source", op.packageSourceFolder];
            
            // auto res = execute(args1);
        }
        else if (exists(pluginId))
        {
            assert(0, "Folders are unimplemented");
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
                .find!((string a) => a.extension == ".nupkg")
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