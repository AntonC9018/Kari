using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[CheckBuildProjectConfigurations]
class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    const string DefaultBuildOutputFolderName = "build_folder";
    [Parameter($"Absolute path where to output kari built things. Default is \"{DefaultBuildOutputFolderName}\"")]
    readonly AbsolutePath OutputDirectory = null;


    [Solution] readonly Solution Solution;

    AbsolutePath SourceDirectory => RootDirectory / "source";
    const string KariGeneratorName = "Kari.Generator";
    const string KariAnnotatorName = "Kari.Annotator";
    /*
    AbsolutePath GetProjectPath(string name) => SourceDirectory / name / (name + ".csproj");
    AbsolutePath KariGeneratorProject => GetProjectPath(KariGeneratorName);
    AbsolutePath KariAnnotatorProject => GetProjectPath(KariAnnotatorName);
    */
    AbsolutePath BuildOutputDirectory => OutputDirectory ?? (RootDirectory / DefaultBuildOutputFolderName);
    AbsolutePath InternalPluginsDirectory => SourceDirectory / "Kari.Plugins";


    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("Generated").ForEach(DeleteDirectory);
            SourceDirectory.GlobFiles("*.[gG]enerated.cs").ForEach(DeleteFile);
            DeleteDirectory(BuildOutputDirectory);

            EnsureCleanDirectory(SourceDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore();
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            var generatorProject = Solution.GetProject(KariGeneratorName);
            var msbuildProject = generatorProject.GetMSBuildProject();

            DotNetBuild(o => o
               .SetProjectFile(generatorProject)
               .SetConfiguration(Configuration)
               .SetFramework("net6.0")
               .SetOutputDirectory(BuildOutputDirectory));
        });
}
