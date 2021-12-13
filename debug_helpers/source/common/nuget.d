module common.nuget;

import common.path_config;
import common.util;

import std.ascii : newline, isDigit;
import std.typecons : tuple;
import std.process;
    
immutable defaultNugetExecutableDirectoryPath = tempFolder;
immutable defaultNugetExecutablePath = defaultNugetExecutableDirectoryPath.buildPath("nuget.exe");
immutable defaultNugetSourcesOutput = buildFolder.buildPath("nuget_sources");

private mixin Versions;
static assert(Version.Windows, "Can't do this on Linux, I don't care.");

void initNuget(string nugetExecutablePath = defaultNugetExecutablePath)
{
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
}

/// Call after you have done `initNuget`
string[string] getNugetList(string nugetExecutablePath)
{
    auto nugetList = execute([nugetExecutablePath, "locals", "all", "-list"]);
    assert(nugetList.status == 0);
    auto nugetPaths = nugetList.output
        .split(newline)
        .map!((a) { auto b = a.findSplit(": "); return tuple(b[0], b[2]); })
        .filter!`a[1].length > 0`
        .assocArray;
    return nugetPaths;
}


import std.path;
import std.file;


struct NugetPackageNameInfo
{
    string nameWithExtension;
    string nameWithVersion;
    size_t indexOfVersionStart;
    string versionString() { return nameWithVersion[indexOfVersionStart .. $]; }
    string name() { return nameWithVersion[0 .. indexOfVersionStart - 1]; }
    string getNugetOutputPathRelativeToSources()
    {
        return name.toLower().buildPath(versionString);
    }
}

NugetPackageNameInfo getPackageNameInfo(string nameWithExtension_)
{
    import std.string : toLower;
    NugetPackageNameInfo result;
    result.nameWithExtension = nameWithExtension_;
    with (result)
    {
        nameWithVersion = nameWithExtension.setExtension("");
        indexOfVersionStart = (){
            size_t index = 0;
            while (index < nameWithVersion.length)
            {
                while (!nameWithVersion.empty && nameWithVersion[index] != '.')
                    index++;
                if (nameWithVersion.empty)
                    return index;
                index++;
                if (isDigit(nameWithVersion[index]))
                    return index;
                index++;
            }
            return index;
        }();
    }
    return result;
}

NugetPackageNameInfo getPackageNameInfo(DirEntry packageArchiveEntry)
{
    return getPackageNameInfo(packageArchiveEntry.name.baseName);
}

struct NugetPackageInfo
{
    DirEntry dirEntry;
    NugetPackageNameInfo nameInfo;
    alias nameInfo this;
}

/// Scans the directory getting the latest modified version and returns the info of that.
NugetPackageInfo getInfoOfLatestPackage(string packageDirectoryWithDifferentVersionsPath)
{
    NugetPackageInfo result;
    result.dirEntry = getEntryWithLatestChange(packageDirectoryWithDifferentVersionsPath);
    result.nameInfo = getPackageNameInfo(result.dirEntry);
    return result;
}
