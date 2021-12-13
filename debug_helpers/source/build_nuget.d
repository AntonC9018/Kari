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
}

string getoptMixin()
{
    auto ret = "auto helpInformation = getopt(args";
    static foreach (field; Options.tupleof)
    {
        import std.format;
        ret ~= `, "%s", "%s", &op.%1$s`.format(__traits(identifier, field), __traits(getAttributes, field)[0]);
    }
    ret ~= ");";
    return ret;
}

void main(string[] args)
{
    Options op;
    auto helpInformation = getOptions(args, op);
    if (helpInformation.helpWanted)
    {
        defaultGetoptPrinter("Help message", helpInformation.options);
        return;
    }

    immutable nupkgSourcesOutput = defaultNugetSourcesOutput;
    if (!exists(tempFolder))
        mkdirRecurse(tempFolder);
    if (!exists(nupkgSourcesOutput))
        mkdirRecurse(nupkgSourcesOutput);

    auto nugetExecutablePath = defaultNugetExecutablePath;
    initNuget(nugetExecutablePath);
    auto nugetPaths = getNugetList(nugetExecutablePath);

    if (op.createFeed)
    {
        if (!executeShell("dotnet nuget list source").output.canFind("kariTestSource [Enabled]"))
        {
            auto r = execute(["dotnet", "nuget", "add", "source", nupkgSourcesOutput, "--name", "kariTestSource"]);
            assert(r.status == 0);
        }
    }

    // writeln(nugetPaths["global-packages"]);
    // writeln(nugetPaths["plugins-cache"]);

    if (op.repackage)
        executeShell("dotnet pack --configuration Release").output.writeln;

    import std.parallelism : parallel;
    foreach (DirEntry folder; dirEntries(nupkgFolder, SpanMode.shallow))//.parallel(1))
    {
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
        immutable packageCachedPath = nugetPaths["global-packages"].buildPath(); 
        if (exists(packageCachedPath))
            rmdirRecurse(packageCachedPath);

        // It does some it seems undocumented magic, I could check the source code but meh.
        // Also apparently it watches my stuff build?? which is kinda cool but unexpected.
        auto n = execute([nugetExecutablePath, "add", info.dirEntry.name, "-Source", nupkgSourcesOutput]);
        writeln("Copied ", info.name, " version ", info.versionString);
        writeln(n.output);
    }
}