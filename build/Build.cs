using System;
using System.Diagnostics;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Serilog;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[CheckBuildProjectConfigurations]
partial class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.CompileGenerator);

    [Solution] readonly Solution Solution;

    const string KariGeneratorName = "Kari.Generator";
    const string KariAnnotatorName = "Kari.Annotator";
    /*
    AbsolutePath GetProjectPath(string name) => SourceDirectory / name / (name + ".csproj");
    AbsolutePath KariGeneratorProject => GetProjectPath(KariGeneratorName);
    AbsolutePath KariAnnotatorProject => GetProjectPath(KariAnnotatorName);
    */
    AbsolutePath SourceDirectory => RootDirectory / "source";
    

    BuildParameters _ops;

    // public struct PathInfo
    // {
    //     public AbsolutePath BaseOutputPath;
    //     public AbsolutePath OutputPath;
    //     public AbsolutePath BaseIntermediateOutputPath;
    //     public AbsolutePath IntermediateOutputPath;
    // }

    AbsolutePath GetProjectBaseOutputPath(string projectName) => _ops.BuildOutputDirectory / projectName;
    AbsolutePath GetProjectOutputPath(string projectName, string configuration) => GetProjectBaseOutputPath(projectName) / configuration;
    AbsolutePath GetBaseIntermediateOutputPath(string projectName) => _ops.BuildOutputDirectory / projectName;
    AbsolutePath GetIntermediateOutputPath(string projectName, string configuration) => GetBaseIntermediateOutputPath(projectName) / configuration;
    AbsolutePath GetPackageOutputPath(string projectName, string configuration) => _ops.BuildOutputDirectory / projectName / configuration;
    
    AbsolutePath InternalPluginsDirectory => SourceDirectory / "Kari.Plugins";

    BuildParameters Parameters { get; set; }
    protected override void OnBuildInitialized()
    {
        Parameters = new BuildParameters(this);
        Log.Information("Building version {0} of Kari ({1}) using version {2} of Nuke.",
            Parameters.Version,
            Configuration,
            typeof(NukeBuild).Assembly.GetName().Version.ToString());

        static void ExecWait(string preamble, string command, string args)
        {
            Console.WriteLine(preamble);
            Process.Start(new ProcessStartInfo(command, args) {UseShellExecute = false}).WaitForExit();
        }
        ExecWait("dotnet version:", "dotnet", "--info");
        ExecWait("dotnet workloads:", "dotnet", "workload list");
    }

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("Generated").ForEach(DeleteDirectory);
            SourceDirectory.GlobFiles("*.[gG]enerated.cs").ForEach(DeleteFile);
            EnsureCleanDirectory(BuildOutputDirectory);
        });

    void ExecuteCompileProject(string name)
    {
        var generatorProject = Solution.GetProject(name);
            
        DotNetRestore(settings => settings
            .SetProjectFile(generatorProject.Path));

        DotNetBuild(settings => settings
            .SetConfiguration(Configuration)
            .SetProjectFile(generatorProject.Path)
            .SetNoRestore(true));
    }

    Target RestoreGenerator => _ => _
        .Executes(() =>
        {
            var project = Solution.GetProject(KariGeneratorName);
            DotNetRestore(settings => settings
                .SetProjectFile(project));
        });
    Target CompileGenerator => _ => _
        .DependsOn(RestoreGenerator)
        .Executes(() => ExecuteCompileProject(KariGeneratorName));

    Target RestoreAnnotator => _ => _
        .Executes(() =>
        {
            var project = Solution.GetProject(KariAnnotatorName);
            DotNetRestore(settings => settings
                .SetProjectFile(project));
        });
    Target CompileAnnotator => _ => _
        .DependsOn(RestoreAnnotator)
        .Executes(() => ExecuteCompileProject(KariAnnotatorName));
    
    /*
        Bootstrap current version by building an older version separately.
        Building the tools (the generator and the annotator).
        Building these separately.
        To do that, restore exactly their dependencies (I guess I need separate restores for these).
        Publishing packages to nuget.
        Sharing configuration with external plugins.
        Internal plugins:
            - Running the annotator, taking the configuration from somewhere;
            - Including the file it generates in clean;
            - Make them depend on both the generator and the annotator being compiled.
        External plugins:
            - Sharing configuration somehow (it's possible);
            - Reusing the configuration for internal plugins.
        Tests:
            - Make them depend on the generator having been built;
            - Make them depend on the corresponding plugin having been built (by name? or by property in config file);
            - Make kari run before it's getting compiled;
        Tools:
            - Install Kari as tool globally;
            - Install the annotator as tool;
            - Kari should output help in nuke compatible html;
            - Use nuke for running the code generator and doing Unity builds???
        Using git for versioning.
        Overriding the currently installed global nuget package and the tools with the temp debug version.
        Output files to the intermediate obj folder, and not together with other files.
        
        That's all??
    */
}
