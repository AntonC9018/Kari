module build_nuget;

import common;

import std.path;
import std.file;    
import std.process;

/*
This script can be used to test nuget package output.
Running this script will add the built packages into the local nuget feed.

Running:
dub --config=build_nuget -- --repackage --createFeed

Getting help:
dub --config=build_nuget -- --help

Now this script is really slow, doing everything synchronously and calling into the atrociously
slow dotnet cli and nuget cli, but it still saves time over running everything manually.
*/
struct Options
{
    @("Whether to call dotnet pack")
    bool repackage = false;

    @("Whether to create the local feed. Pass this flag when running it for the first time.")
    bool createFeed = false;

    @("Clear all previous packages before rebuilding. There might be no other way of testing local changes.")
    bool clearAll = false;

    @("Whether to do a public relese (sane plugin version number).")
    bool doPublicRelease = true;
}

void main(string[] args)
{
    Options op;
    auto helpInformation = getOptions(args, op);
    if (helpInformation.helpWanted)
    {
        const help = `Release builds all projects into packages.
Clears nuget cache for it to register the latest versions of the packages correctly.
Use this script in order to use the latest versions in your custom standalone plugin projects, when developing both the plugin and Kari.`;
        defaultGetoptPrinter(help, helpInformation.options);
        return;
    }

    immutable nupkgSourcesOutput = absolutePath(defaultNugetSourcesOutput);
    if (!exists(tempFolder))
        mkdirRecurse(tempFolder);
    if (!exists(nupkgSourcesOutput))
    {
        mkdirRecurse(nupkgSourcesOutput);
    }
    else if (op.clearAll)
    {
        rmdirRecurse(nupkgSourcesOutput);
        mkdirRecurse(nupkgSourcesOutput);
    }

    auto nugetExecutablePath = defaultNugetExecutablePath;
    initNuget(nugetExecutablePath);
    auto nugetPaths = getNugetList(nugetExecutablePath);

    if (op.createFeed)
    {
        const sources = executeShell("dotnet nuget list source").output;
        const sourceName = "kariTestSource";
        auto linesFrom = splitter(sources, "\n")
            .find!(
                a => a.canFind(
                    sourceName.chain(" [Enabled]")));

        void add()
        {
            auto r = execute(["dotnet", "nuget", "add", "source", nupkgSourcesOutput, "--name", sourceName]);
            assert(r.status == 0);
        }
        if (linesFrom.empty)
        {
            add();
        }
        else
        {
            linesFrom.popFront();
            assert(!linesFrom.empty);

            import std.string : stripLeft;
            const actualPathToSources = stripLeft(linesFrom.front);

            if (!filenameCmp(defaultNugetSourcesOutput, actualPathToSources))
            {
                writeln("The packages source ", sourceName, " will be recreated.");
                auto r = execute(["dotnet", "nuget", "remove", "source", sourceName]);
                assert(r.status == 0);
                add();
            }
        }
    }

    // writeln(nugetPaths["global-packages"]);
    // writeln(nugetPaths["plugins-cache"]);


    if (op.repackage)
    {
        auto dotnetArgs = ["dotnet", "pack", "--configuration", "Release"];
        if (op.doPublicRelease)
            dotnetArgs ~= "/p:PublicRelease=true";
        execute(dotnetArgs).output.writeln;
    }

    import std.parallelism : parallel;
    foreach (DirEntry folder; dirEntries(nupkgFolder, SpanMode.shallow).parallel(1))
    { 
        try {

        immutable info = getInfoOfLatestPackage(folder.name.buildPath("Release"));
        immutable outputPath = buildPath(nupkgSourcesOutput, info.getNugetOutputPathRelativeToSources());
        
        // do a little optimization because nuget takes long to check folders
        if (exists(outputPath))
        {
            const outputEntry = DirEntry(outputPath);
            if (info.dirEntry.timeLastModified <= outputEntry.timeLastModified)
                continue;
            rmdirRecurse(outputPath);
        }

        // Clear its cache manually because beats me
        immutable packageCachedPath = nugetPaths["global-packages"].buildPath(info.getNugetOutputPathRelativeToSources); 
        if (exists(packageCachedPath))
            rmdirRecurse(packageCachedPath);

        // It does some it seems undocumented magic, I could check the source code but meh.
        // Also apparently it watches my stuff build?? which is kinda cool but unexpected.
        auto n = execute([nugetExecutablePath, "add", info.dirEntry.name, "-Source", nupkgSourcesOutput]);
        writeln("Copied ", info.name, " version ", info.versionString);
        writeln(n.output);
        
        } catch (Exception e) { writeln(e); continue; }
    }
}