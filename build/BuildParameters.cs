// Adapted from https://github.com/AvaloniaUI/Avalonia
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.CI.AzurePipelines;
using Nuke.Common.IO;
using static Nuke.Common.IO.PathConstruction;

partial class Build
{
    [Parameter("configuration")]
    Configuration Configuration { get; set; }

    [Parameter("skip-tests")]
    bool SkipTests { get; set; }

    [Parameter("force-nuget-version")]
    string ForceNugetVersion { get; set; }

    const string DefaultBuildOutputFolderName = "build_folder";
    [Parameter($"Absolute path where to output kari built things. Default is \"{DefaultBuildOutputFolderName}\"")]
    readonly AbsolutePath KariBuildPath = null;

    public class BuildParameters
    {
        public string Configuration { get; }
        public bool SkipTests { get; }
        public bool SkipPreviewer {get;}
        public string MainRepo { get; }
        public string MasterBranch { get; }
        public string RepositoryName { get; }
        public string RepositoryBranch { get; }
        public string ReleaseConfiguration { get; }
        public string ReleaseBranchPrefix { get; }
        public string MSBuildSolution { get; }
        public bool IsLocalBuild { get; }
        public bool IsRunningOnUnix { get; }
        public bool IsRunningOnWindows { get; }
        public bool IsRunningOnAzure { get; }
        public bool IsPullRequest { get; }
        public bool IsMainRepo { get; }
        public bool IsMasterBranch { get; }
        public bool IsReleaseBranch { get; }
        public bool IsReleasable { get; }
        public bool IsMyGetRelease { get; }
        public bool IsNuGetRelease { get; }
        public bool PublishTestResults { get; }
        public string Version { get; }
        public AbsolutePath ArtifactsDir { get; }
        public AbsolutePath NugetIntermediateRoot { get; }
        public AbsolutePath NugetRoot { get; }
        public AbsolutePath ZipRoot { get; }
        public AbsolutePath BinRoot { get; }
        public AbsolutePath TestResultsRoot { get; }
        public string DirSuffix { get; }
        public List<string> BuildDirs { get; }
        public string FileZipSuffix { get; }
        public AbsolutePath ZipCoreArtifacts { get; }
        public AbsolutePath ZipNuGetArtifacts { get; }
        public AbsolutePath ZipTargetControlCatalogNetCoreDir { get; }


        public BuildParameters(Build b)
        {
            // ARGUMENTS
            Configuration = b.Configuration ?? global::Configuration.Release;
            SkipTests = b.SkipTests;

            // CONFIGURATION
            MainRepo = "https://github.com/AntonC9018/Kari";
            MasterBranch = "refs/heads/master";
            ReleaseBranchPrefix = "refs/heads/release/";
            ReleaseConfiguration = "Release";
            MSBuildSolution = RootDirectory / "dirs.proj";

            // PARAMETERS
            IsLocalBuild = true;
            IsRunningOnUnix = Environment.OSVersion.Platform == PlatformID.Unix ||
                              Environment.OSVersion.Platform == PlatformID.MacOSX;
            IsRunningOnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            IsRunningOnAzure = false;

            if (IsRunningOnAzure)
            {
                RepositoryName = AzurePipelines.Instance.RepositoryUri;
                RepositoryBranch = AzurePipelines.Instance.SourceBranch;
                IsPullRequest = AzurePipelines.Instance.PullRequestId.HasValue;
                IsMainRepo = StringComparer.OrdinalIgnoreCase.Equals(MainRepo, AzurePipelines.Instance.RepositoryUri);
            }
            IsMainRepo =
                StringComparer.OrdinalIgnoreCase.Equals(MainRepo,
                    RepositoryName);
            IsMasterBranch = StringComparer.OrdinalIgnoreCase.Equals(MasterBranch,
                RepositoryBranch);
            IsReleaseBranch = RepositoryBranch?.StartsWith(ReleaseBranchPrefix, StringComparison.OrdinalIgnoreCase) ==
                              true;

            IsReleasable = StringComparer.OrdinalIgnoreCase.Equals(ReleaseConfiguration, Configuration);
            IsMyGetRelease = IsReleasable;
            IsNuGetRelease = IsMainRepo && IsReleasable && IsReleaseBranch;

            // VERSION
            Version = b.ForceNugetVersion ?? GetVersion();

            if (IsRunningOnAzure)
            {
                if (!IsNuGetRelease)
                {
                    // Use AssemblyVersion with Build as version
                    Version += "-cibuild" + int.Parse(Environment.GetEnvironmentVariable("BUILD_BUILDID")).ToString("0000000") + "-beta";
                }

                PublishTestResults = true;
            }

            // DIRECTORIES
            ArtifactsDir = RootDirectory / "artifacts";
            NugetRoot = ArtifactsDir / "nuget";
            NugetIntermediateRoot = RootDirectory / "build-intermediate" / "nuget";
            ZipRoot = ArtifactsDir / "zip";
            BinRoot = ArtifactsDir / "bin";
            TestResultsRoot = ArtifactsDir / "test-results";
            BuildDirs = GlobDirectories(RootDirectory, "**bin").Concat(GlobDirectories(RootDirectory, "**obj")).ToList();
            DirSuffix = Configuration;
            FileZipSuffix = Version + ".zip";
            ZipCoreArtifacts = ZipRoot / ("Avalonia-" + FileZipSuffix);
            ZipNuGetArtifacts = ZipRoot / ("Avalonia-NuGet-" + FileZipSuffix);
            ZipTargetControlCatalogNetCoreDir = ZipRoot / ("ControlCatalog.NetCore-" + FileZipSuffix);
        }

        string GetVersion()
        {
            var xdoc = XDocument.Load(RootDirectory / "build/SharedVersion.props");
            return xdoc.Descendants().First(x => x.Name.LocalName == "Version").Value;
        }
    }

}
