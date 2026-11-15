using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Editor
{
    // Command-line development build for local testing:
    //   Unity -batchmode -quit -projectPath . -executeMethod Editor.DevBuild.BuildLinux
    public static class DevBuild
    {
        public static void BuildLinux()
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = "Builds/DevLinux/YARG",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.Development,
                // Marks this as a test build: it uses the "dev" data folder and the dev asset
                // catalog instead of sharing the release ones.
                extraScriptingDefines = new[] { "YARG_TEST_BUILD" },
            };

            var report = BuildPipeline.BuildPlayer(options);
            Console.WriteLine($"DevBuild result: {report.summary.result}, errors: {report.summary.totalErrors}");
            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
