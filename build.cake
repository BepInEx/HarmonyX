#addin nuget:?package=Cake.FileHelpers&version=3.2.1
#tool "nuget:?package=NUnit.ConsoleRunner"

var target = Argument("target", "Build");
var nugetKey = Argument("nugetKey", "");

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

Task("PullDependencies")
    .Does(() =>
{
    Information("Restoring NuGet packages");
    NuGetRestore("./Harmony.sln");
});

Task("Build")
    .IsDependentOn("Cleanup")
    .IsDependentOn("PullDependencies")
    .Does(() =>
{
    var buildSettings = new MSBuildSettings {
        Configuration = "Release",
        Restore = true
    };
    buildSettings.Targets.Add("build");
    buildSettings.Targets.Add("pack");
    MSBuild("./Harmony.sln", buildSettings);
});

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
{
    NUnit3("./HarmonyTests/bin/Release/**/HarmonyTests.dll", new NUnit3Settings {
        NoResults = true
    });
});

Task("Publish")
    .IsDependentOn("Build")
    .Does(() => 
{
    var version = FindRegexMatchGroupInFile("./Harmony/Harmony.csproj", @"<Version>(.*)<\/Version>", 1, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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