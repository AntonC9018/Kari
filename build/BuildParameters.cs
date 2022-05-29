// Adapted from https://github.com/AvaloniaUI/Avalonia
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.CI.AzurePipelines;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using static Nuke.Common.IO.PathConstruction;

partial class Build
{
    [Parameter("configuration")]
    public Configuration Configuration;

    [Parameter("skip-tests")]
    public bool SkipTests;

    [Parameter("force-nuget-version")]
    public string ForceNugetVersion;

    public const string DefaultBuildOutputFolderName = "build_folder";
    [Parameter($"Absolute path where to output kari built things. Default is \"{DefaultBuildOutputFolderName}\"")]
    public AbsolutePath KariBuildOutputDirectory = null;

    [Parameter($"Names of internal plugins to build")]
    public string[] PluginsToBuild = null;

    public class BuildParameters
    {
        public bool SkipTests { get; }
        public string MainRepo { get; }
        public string MasterBranch { get; }
        public string RepositoryName { get; }
        public string RepositoryBranch { get; }
        public string ReleaseBranchPrefix { get; }
        public bool IsRunningOnUnix { get; }
        public bool IsRunningOnWindows { get; }
        public bool IsMainRepo { get; }
        public bool IsMasterBranch { get; }
        public bool IsReleaseBranch { get; }
        public bool IsReleasable { get; }
        public bool IsMyGetRelease { get; }
        public bool IsNuGetRelease { get; }
        public string Version { get; }
        public AbsolutePath ArtifactsDir { get; }
        public AbsolutePath NugetIntermediateRoot { get; }
        public AbsolutePath NugetRoot { get; }
        public AbsolutePath ZipRoot { get; }
        public AbsolutePath BinRoot { get; }
        public AbsolutePath TestResultsRoot { get; }
        public string DirSuffix { get; }
        public List<string> BuildDirs { get; }

        public BuildParameters(Build b)
        {
            // ARGUMENTS
            b.Configuration ??= (IsLocalBuild ? Configuration.Debug : Configuration.Release);
            SkipTests = b.SkipTests;

            // CONFIGURATION
            MainRepo = "https://github.com/AntonC9018/Kari";
            MasterBranch = "refs/heads/master";
            ReleaseBranchPrefix = "refs/heads/release/";

            // PARAMETERS
            IsRunningOnUnix = Environment.OSVersion.Platform == PlatformID.Unix ||
                              Environment.OSVersion.Platform == PlatformID.MacOSX;
            IsRunningOnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

            IsMainRepo =
                StringComparer.OrdinalIgnoreCase.Equals(MainRepo,
                    RepositoryName);
            IsMasterBranch = StringComparer.OrdinalIgnoreCase.Equals(MasterBranch,
                RepositoryBranch);
            IsReleaseBranch = RepositoryBranch?.StartsWith(ReleaseBranchPrefix, StringComparison.OrdinalIgnoreCase) ==
                              true;

            IsReleasable = b.Configuration == Configuration.Release;
            IsMyGetRelease = IsReleasable;
            IsNuGetRelease = IsMainRepo && IsReleasable && IsReleaseBranch;

            // VERSION
            // TODO: https://gitversion.net/
            Version = b.ForceNugetVersion ?? "1.2.0";

            // DIRECTORIES
            b.KariBuildOutputDirectory ??= (RootDirectory / Build.DefaultBuildOutputFolderName);
            ArtifactsDir = b.KariBuildOutputDirectory / "artifacts";
            NugetRoot = ArtifactsDir / "nuget";
            NugetIntermediateRoot = b.KariBuildOutputDirectory / "nuget-intermediate" / "nuget";
            ZipRoot = ArtifactsDir / "zip";
            BinRoot = ArtifactsDir / "bin";
            TestResultsRoot = ArtifactsDir / "test-results";
            BuildDirs = GlobDirectories(RootDirectory, "**bin").Concat(GlobDirectories(RootDirectory, "**obj")).ToList();
            DirSuffix = b.Configuration;

            if (b.PluginsToBuild == null || !b.PluginsToBuild.Any())
                b.PluginsToBuild = Helper.GetAllPluginDirectoryNames(b.InternalPluginsDirectory).ToArray();

            // TODO: check that the plugins list is null if target that's requested is not for building plugins.
            // b.ExecutionPlan.Contains(target => target.
        }

        string GetVersion()
        {
            var xdoc = XDocument.Load(RootDirectory / "build/SharedVersion.props");
            return xdoc.Descendants().First(x => x.Name.LocalName == "Version").Value;
        }
    }

}

public static class Helper
{
    public static IEnumerable<string> GetAllPluginDirectoryNames(string internalPluginsDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(internalPluginsDirectory))
        {
            int directoryNameStartIndex = directory.LastIndexOf(Path.DirectorySeparatorChar) + 1;
            string directoryName = directory[directoryNameStartIndex ..];
            if (directoryName.Contains(".Tests"))
                continue;

            yield return directoryName;
        }
        // return 
        //     .Select(p => p[( + 1) ..])
        //     .Where(name => !name.Contains(".Tests"));
    }
}


public static class ToolSettingsExtensions
{
    public static T Apply<T>(this T settings, Configure<T> configurator)
    {
        Assert.NotNull(configurator);
        return configurator(settings);
    }
}