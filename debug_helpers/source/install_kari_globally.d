module install_kari_globally;

import common;

import std.path;
import std.file;    
import std.process;

/*
Use this script to install latest compiled kari version.
It essentially just does `dotnet tool install`, but it will find the latest version dynamically.
*/
struct Options
{
    @("Whether to call dotnet pack before installing kari.")
    bool repackage = false;

    @("The configuration")
    string configuration = "Debug";
}

int main(string[] args)
{
    Options op;
    auto helpInformation = getOptions(args, op);
    if (helpInformation.helpWanted)
    {
        defaultGetoptPrinter("Installs kari globally. Optionally builds it before installing.", helpInformation.options);
        return 0;
    }

    immutable nupkgSourcesOutput = defaultNugetSourcesOutput;
    if (!exists(nupkgSourcesOutput))
    {
        if (!op.repackage)
            writeln("The package output folder did not exist, Kari will be rebuilt.");
        op.repackage = true;
    }

    if (op.repackage)
    {
        const result = execute(["dotnet", "pack", "--configuration", op.configuration, `source\Kari.Generator`]);
        writeln(result.output);
        if (result.status != 0)
            return 1;
    }

    auto entries = dirEntries(`build_folder/nuget_sources/kari.generator`, SpanMode.shallow).array;
    auto sorted = entries.sort!((a, b) => a.timeLastModified > b.timeLastModified).release;
    auto greatest = sorted[0].baseName;
    writeln(greatest); // nbgv get-version "NuGetPackageVersion"

    executeShell("dotnet tool uninstall --global Kari.Generator").output.writeln;
    execute([
        "dotnet", "tool", "install", 
        "Kari.Generator", 
        "--global", 
        "--add-source", `build_folder\.nupkg\Kari.Generator\Debug`, 
        "--version",
        greatest])
            .output.writeln;

    return 0;
}