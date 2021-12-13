// dmd -g -m64 copy_nuget.d 
// copy_nuget

void main()
{
    import std.path;
    import std.file;
    import std.range;
    import std.stdio;
    import std.algorithm;
    import std.ascii : isDigit;
    import std.string : toLower;
    import std.conv : to;
    import std.parallelism;
    import std.process;

    version (Windows) {} else static assert(0, "Can't do this on linux, I don't care.");

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
        assert(result.status != 0, "Curl failed: " ~ result.output);
    }();
    assert(exists(nugetExecutablePath), "No expected path");

    // executeShell("dotnet pack --configuration Release").output.writeln;

    foreach (DirEntry folder; dirEntries(nupkgFolder, SpanMode.shallow))
    {
        immutable folderPath  = folder.name.buildPath("Release");
        immutable packagePath = dirEntries(folderPath, SpanMode.shallow)
            .array
            .sort!((DirEntry a, DirEntry b) => a.timeLastModified > b.timeLastModified)
            .release.front.name;

        // do a little optimization because nuget takes long to check folders
        immutable packageNameWithExt = packagePath.baseName;
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
        immutable version_      = package_[indexOfVersionStart .. $];
        immutable outputPath    = buildPath(nupkgSourcesOutput, package_[0 .. indexOfVersionStart - 1].toLower, version_);
        if (exists(outputPath))
        {
            const outputEntry = DirEntry(outputPath);
            if (folder.timeLastModified <= outputEntry.timeLastModified)
                continue;
        }

        // It does some it seems undocumented magic, I could check the source code but meh.
        // Also apparently it watches my stuff build?? which is kinda cool but unexpected.
        auto n = execute([nugetExecutablePath, "add", packagePath, "-Source", nupkgSourcesOutput]);
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