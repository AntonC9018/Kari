// Adapted from https://github.com/AvaloniaUI/Avalonia
using System;
using System.Collections.Generic;
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
    public Configuration Configuration { get; set; }

    [Parameter("skip-tests")]
    public bool SkipTests { get; set; }

    [Parameter("force-nuget-version")]
    public string ForceNugetVersion { get; set; }

    public const string DefaultBuildOutputFolderName = "build_folder";
    [Parameter($"Absolute path where to output kari built things. Default is \"{DefaultBuildOutputFolderName}\"")]
    public readonly AbsolutePath KariBuildOutputDirectory = null;

    [Parameter($"Names of internal plugins to build")]
    public readonly AbsolutePath PluginsToBuild = null;

    public class BuildParameters
    {
        public AbsolutePath BuildOutputDirectory { get; }
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
            BuildOutputDirectory = b.KariBuildOutputDirectory ?? (RootDirectory / Build.DefaultBuildOutputFolderName);
            ArtifactsDir = BuildOutputDirectory / "artifacts";
            NugetRoot = ArtifactsDir / "nuget";
            NugetIntermediateRoot = BuildOutputDirectory / "nuget-intermediate" / "nuget";
            ZipRoot = ArtifactsDir / "zip";
            BinRoot = ArtifactsDir / "bin";
            TestResultsRoot = ArtifactsDir / "test-results";
            BuildDirs = GlobDirectories(RootDirectory, "**bin").Concat(GlobDirectories(RootDirectory, "**obj")).ToList();
            DirSuffix = b.Configuration;
        }

        string GetVersion()
        {
            var xdoc = XDocument.Load(RootDirectory / "build/SharedVersion.props");
            return xdoc.Descendants().First(x => x.Name.LocalName == "Version").Value;
        }
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