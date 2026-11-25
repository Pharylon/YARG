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
            Build("Builds/DevLinux/YARG", BuildOptions.Development, "YARG_DEV_BUILD");
        }

        // Non-development build for playtesting on another machine:
        //   Unity -batchmode -quit -projectPath . -executeMethod Editor.DevBuild.BuildLinuxPlaytest
        public static void BuildLinuxPlaytest()
        {
            Build("Builds/PlaytestLinux/YARG", BuildOptions.None, "YARG_TEST_BUILD");
        }

        private static void Build(string location, BuildOptions buildOptions, string buildType)
        {
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = location,
                target = BuildTarget.StandaloneLinux64,
                options = buildOptions,
                // The build type picks the data folder and asset catalog: YARG_DEV_BUILD uses "dev";
                // YARG_TEST_BUILD uses "testing", which starts as a copy of the nightly or release data.
                extraScriptingDefines = new[] { buildType },
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
