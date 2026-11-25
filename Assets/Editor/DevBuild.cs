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
            Build("Builds/DevLinux/YARG", BuildOptions.Development);
        }

        // Non-development build for playtesting on another machine:
        //   Unity -batchmode -quit -projectPath . -executeMethod Editor.DevBuild.BuildLinuxPlaytest
        public static void BuildLinuxPlaytest()
        {
            Build("Builds/PlaytestLinux/YARG", BuildOptions.None);
        }

        private static void Build(string location, BuildOptions buildOptions)
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = location,
                target = BuildTarget.StandaloneLinux64,
                options = buildOptions,
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
