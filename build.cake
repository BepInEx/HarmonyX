#addin nuget:?package=Cake.FileHelpers&version=4.0.0
#tool "nuget:?package=NUnit.ConsoleRunner&version=3.12.0"
#addin "nuget:?package=Cake.Incubator&version=6.0.0"

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
    DotNetRestore(".");
});

Task("Build")
    .IsDependentOn("Cleanup")
    .IsDependentOn("PullDependencies")
    .Does(() =>
{
    var buildSettings = new DotNetBuildSettings {
        Configuration = "Release",
		MSBuildSettings = new DotNetCoreMSBuildSettings()
    };
    buildSettings.MSBuildSettings.Targets.Add("build");
    buildSettings.MSBuildSettings.Targets.Add("pack");
    DotNetBuild(".", buildSettings);
});

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
{
	var testTargets = new [] { "net35", "netcoreapp3.1", "net6.0", "net7.0" };
	foreach (var target in testTargets)
	{
	    Information($"Testing {target}");
		DotNetCoreTest("./HarmonyTests/HarmonyTests.csproj", new DotNetCoreTestSettings {
			Configuration = "Release",
			Framework = target,
			Verbosity = DotNetCoreVerbosity.Normal
		});
	}
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
