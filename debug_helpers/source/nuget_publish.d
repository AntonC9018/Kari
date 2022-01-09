module nuget_publish;

import common;

import std.path;
import std.file;    
import std.typecons : tuple;
import std.process;

auto toTuple2(T)(T a)
{
    auto arr = staticArray!2(a);
    return tuple(arr[0], arr[1]);
}

auto colonValueToAssocArray(string a)
{
    import std.string : strip;
    return a
        .strip
        .split("\n")
        .map!(
            line => line
                .split(":")
                .map!(a => strip(a))
                .toTuple2)
        .assocArray;
}

struct Options
{
    @("Whether to call dotnet pack")
    bool build = true;

    @("Clear all previous packages before rebuilding.")
    bool clearAll = false;
}

int main(string[] args)
{
    Options op;
    auto helpInformation = getOptions(args, op);
    if (helpInformation.helpWanted)
    {
        const help = `Builds and publishes everything to nuget.`;
        defaultGetoptPrinter(help, helpInformation.options);
        return 0;
    }

    const nerdbankGetVersionResult = executeShell("nbgv get-version");
    if (nerdbankGetVersionResult.status != 0)
    {
        writeln("nbgv failed:");
        writeln(nerdbankGetVersionResult.output);
        return 1;
    }

    if (op.clearAll)
        rmdirRecurse(nupkgFolder);

    // Build everything
    if (op.build)
    {
        const packResult = executeShell("dotnet pack /p:PublicRelease=true --configuration Release");
        if (packResult.status != 0)
        {
            writeln("dotnet pack maybe failed:");
            writeln(packResult.output);
        }
    }

    const versionsDict = nerdbankGetVersionResult.output.colonValueToAssocArray;
    const keys = readText("apikey.txt").colonValueToAssocArray;

    auto nugetPackageVersion = versionsDict["NuGetPackageVersion"];
    const nugetEnding = "." ~ nugetPackageVersion ~ ".nupkg";

    if (!isFile("nuget.config"))
    {
        writeln("You cannot push unless there's a nuget config in the root.\nhttps://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry");
        return 1;
    }

    foreach (string buildOutputFolder; dirEntries(nupkgFolder, SpanMode.shallow))
    {
        const releaseFolder = buildPath(buildOutputFolder, "Release");

        if (!exists(releaseFolder))
            continue;

        {
            foreach (string fileName; dirEntries(releaseFolder, SpanMode.shallow))
            {
                if (fileName.endsWith(nugetEnding))
                {
                    const args1 = [
                        "dotnet", "nuget", "push", fileName, 
                        "--api-key", keys["github nuget kari publish"],
                        "--source", "github",
                        "--skip-duplicate"];
                    writeln(args1.join(" "));
                    const result = execute(args1);
                    writeln(result.output);
                    break;
                }
            }
        }
        
        // {
        //     const snugetEnding = "." ~ nugetPackageVersion ~ ".snupkg";
        //     foreach (string fileName; dirEntries(releaseFolder, SpanMode.shallow))
        //     {
        //         if (fileName.endsWith(snugetEnding))
        //             writeln("Publishing ", fileName);
        //     }
        // }
    }

    return 0;
}