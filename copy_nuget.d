/*
This script can be used to test nuget package output.
Running this script will add the built packages into the local nuget feed.

Recompiling:
dmd -g -m64 copy_nuget.d 

Running:
copy_nuget.exe --repackage --createFeed

Getting help:
copy_nuget.exe --help

Now this script is really slow, doing everything synchronously and calling into the atrociously
slow dotnet cli and nuget cli, but it still saves time over running everything manually.
*/
struct Options
{
    @("Whether to call dotnet pack")
    bool repackage = false;

    @("Whether to create the local feed. Pass this flag when running the first time.")
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
    import std.getopt;
    import std.path;
    import std.file;
    import std.range;
    import std.stdio;
    import std.algorithm;
    import std.ascii : isDigit, newline;
    import std.string : toLower;
    import std.conv : to;
    import std.typecons : tuple;
    import std.parallelism;
    import std.process;

    version (Windows) {} else static assert(0, "Can't do this on linux, I don't care.");

    Options op;
    mixin(getoptMixin());
    if (helpInformation.helpWanted)
    {
        defaultGetoptPrinter("Help message", helpInformation.options);
        return;
    }

    immutable buildFolder = "build_folder";
    immutable nupkgFolder = buildFolder.buildPath(".nupkg");
    immutable nupkgSourcesOutput = buildFolder.buildPath("nuget_sources");
    immutable tempFolder  = buildFolder.buildPath("tool_cache");
    mkdirRecurse(tempFolder);
    mkdirRecurse(nupkgSourcesOutput);

    immutable nugetExecutablePath = tempFolder.buildPath("nuget.exe");
    if (!exists(nugetExecutablePath)) 
    (){
        import std.net.curl;
        immutable link = `https://dist.nuget.org/win-x86-commandline/latest/nuget.exe`;
        try return download(link, tempFolder);
        catch (CurlException exc) {} // could not load, defaulting to calling curl.exe.
        auto result = execute(["curl.exe", link, "--output", nugetExecutablePath]);
        assert(result.status == 0, "Curl failed: " ~ result.output);
    }();
    assert(exists(nugetExecutablePath), "No expected path");

    auto nugetList = execute([nugetExecutablePath, "locals", "all", "-list"]);
    assert(nugetList.status == 0);
    auto nugetPaths = nugetList.output
        .split(newline)
        .map!((a) { auto b = a.findSplit(": "); return tuple(b[0], b[2]); })
        .filter!`a[1].length > 0`
        .assocArray;

    if (op.createFeed)
    {
        if (!executeShell("dotnet nuget list source").output.canFind("2.  kariTestSource [Enabled]"))
        {
            auto r = execute(["dotnet", "nuget", "add", "source", nupkgSourcesOutput, "--name", "kariTestSource"]);
            assert(r.status == 0);
        }
    }

    // writeln(nugetPaths["global-packages"]);
    // writeln(nugetPaths["plugins-cache"]);

    if (op.repackage)
        executeShell("dotnet pack --configuration Release").output.writeln;

    foreach (DirEntry folder; dirEntries(nupkgFolder, SpanMode.shallow))
    {
        immutable folderPath   = folder.name.buildPath("Release");
        immutable packageEntry = dirEntries(folderPath, SpanMode.shallow)
            .array
            .sort!((DirEntry a, DirEntry b) => a.timeLastModified > b.timeLastModified)
            .release.front;

        immutable packageNameWithExt = packageEntry.name.baseName;
        immutable package_      = packageNameWithExt.setExtension("");
        immutable indexOfVersionStart = {
            size_t index = 0;
            while (index < package_.length)
            {
                while (!package_.empty && package_[index] != '.')
                    index++;
                if (package_.empty)
                    return index;
                index++;
                if (isDigit(package_[index]))
                    return index;
                index++;
            }
            return index;
        }();
        immutable version_              = package_[indexOfVersionStart .. $];
        immutable packageName           = package_[0 .. indexOfVersionStart - 1];
        immutable packageNameLowercase  = packageName.toLower;
        immutable outputPath = buildPath(nupkgSourcesOutput, packageNameLowercase, version_);
        
        // do a little optimization because nuget takes long to check folders
        if (exists(outputPath))
        {
            const outputEntry = DirEntry(outputPath);
            if (packageEntry.timeLastModified <= outputEntry.timeLastModified)
                continue;
            rmdirRecurse(outputPath);
        }

        // Clear its cache manually because beats me
        immutable packageCachedPath = nugetPaths["global-packages"].buildPath(packageNameLowercase); 
        if (exists(packageCachedPath))
            rmdirRecurse(packageCachedPath);

        // It does some it seems undocumented magic, I could check the source code but meh.
        // Also apparently it watches my stuff build?? which is kinda cool but unexpected.
        auto n = execute([nugetExecutablePath, "add", packageEntry.name, "-Source", nupkgSourcesOutput]);
        writeln("Copied ", packageName, " version ", version_);
        writeln(n.output);
    }

        /*
        mkdirRecurse(outputPath);
        std.file.copy(packagePath, outputPath.buildPath(packageNameWithExt));

        import std.zip;
        auto archive = new ZipArchive(std.file.read(packagePath));
		foreach (name, member; archive.directory) 
        {
            auto path = buildPath(outputPath, name);
            mkdirRecurse(path.dirName);
            archive.expand(member);
            std.file.write(path, member.expandedData);
		}
        */
}