#addin nuget:?package=Cake.FileHelpers&version=7.0.0

var target = Argument("target", "Build");
var nugetKey = Argument("nugetKey", "");
var useTmpLocalNuget = Argument("useTmpLocalNuget", false);

string RunGit(string command, string separator = "") 
{
    using(var process = StartAndReturnProcess("git", new ProcessSettings { Arguments = command, RedirectStandardOutput = true })) 
    {
        process.WaitForExit();
        return string.Join(separator, process.GetStandardOutput());
    }
}

Task("Cleanup")
    .Does(() =>
{
    Information("Cleaning up old build objects");
    CleanDirectories(GetDirectories("./**/bin/"));
    CleanDirectories(GetDirectories("./**/obj/"));
});

Task("Build")
    .IsDependentOn("Cleanup")
    .Does(() =>
{
    var buildSettings = new DotNetBuildSettings {
		MSBuildSettings = new DotNetMSBuildSettings()
    };
    buildSettings.MSBuildSettings.Targets.Add("build");
    buildSettings.MSBuildSettings.Targets.Add("pack");
    buildSettings.Configuration = "Release";
    DotNetBuild("./Harmony/Harmony.csproj", buildSettings);
});

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
{
    var targets = FindRegexMatchGroupInFile("./HarmonyTests/HarmonyTests.csproj", @"<TargetFrameworks>(.*)<\/TargetFrameworks>", 1, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Captures[0].Value.Split(';');

    foreach (var target in targets)
    {
        Information($"Testing {target}");
        DotNetTest("./HarmonyTests/HarmonyTests.csproj", new DotNetTestSettings {
            Configuration = "Release",
            Framework = target,
            Verbosity = DotNetVerbosity.Normal
        });
    }
});

Task("Publish")
    .IsDependentOn("Build")
    .Does(() => 
{
    var version = FindRegexMatchGroupInFile("./Directory.Build.props", @"<HarmonyXVersion>(.*)<\/HarmonyXVersion>", 1, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Captures[0].Value;
    version += FindRegexMatchGroupInFile("./Directory.Build.props", @"<HarmonyXVersionSuffix>(.*)<\/HarmonyXVersionSuffix>", 1, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Captures[0].Value;

    var versionTagPresent = !string.IsNullOrWhiteSpace(RunGit($"ls-remote --tags origin v{version}"));
    if(versionTagPresent) 
    {
        Information("New version exists, no need to push.");
        return;
    }

    Information($"Pushing tag v{version}");
    RunGit($"tag v{version}");
    RunGit($"push origin v{version}");

    if(string.IsNullOrWhiteSpace(nugetKey)){
        Information("No NuGet key specified, can't publish");
        return;
    }

    NuGetPush($"./Harmony/bin/Release/HarmonyX.{version}.nupkg", new NuGetPushSettings {
        Source = "https://api.nuget.org/v3/index.json",
        ApiKey = nugetKey
    });
});

RunTarget(target);
