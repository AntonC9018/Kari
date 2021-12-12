import std.path;
import std.file;
import std.range;
import std.algorithm;
import std.ascii : isDigit;
import std.string : toLower;
import std.conv : to;
import std.parallelism;


void main()
{
    immutable buildFolder = "build_folder";
    immutable nupkgFolder = buildFolder.buildPath(".nupkg");
    immutable nupkgSourcesOutput = buildFolder.buildPath("nuget_sources");

    if (!exists(nupkgSourcesOutput))
        mkdir(nupkgSourcesOutput);

    foreach (folder; dirEntries(nupkgFolder, SpanMode.shallow).parallel(2))
    {
        immutable folderPath    = folder.buildPath("Release");
        immutable packagePath   = dirEntries(folderPath, SpanMode.shallow).front.name;
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
    }
}