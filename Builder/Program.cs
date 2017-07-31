using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using System.Threading;
using Microsoft.Build.Logging;
using Microsoft.Build.Shared;

namespace Builder
{
    class Builder
    {
        private BuildManager _buildManager = BuildManager.DefaultBuildManager;
        private Dictionary<string, string> _buildProperties = new Dictionary<string, string>();
        private ILogger _logger = new ConsoleLogger {Verbosity = LoggerVerbosity.Minimal};
        private string _toolsVersion = "15.0";
        private string _resourcesAbsolutePath;

        public Builder(string resourcesAbsolutePath)
        {
            _resourcesAbsolutePath = resourcesAbsolutePath;
        }

        private BuildRequestData CreateRequest(string projectPath, string target)
        {
            // The current directory is modified by MSBuild when building a project, so the absolute path must be used.
            string projectFullPath = Path.Combine(_resourcesAbsolutePath, projectPath);
            ProjectInstance projectInstance = new ProjectInstance(projectFullPath, _buildProperties, _toolsVersion);
            return new BuildRequestData(projectInstance, new[] { target });
        }

        public void Build(string[] projectPaths, string target, bool parallelBuild, int maxNodeCount = 1)
        {
            Console.WriteLine("========================================");

            BuildParameters buildParameters = new BuildParameters(ProjectCollection.GlobalProjectCollection)
            {
                Loggers = new[] { _logger },
                MaxNodeCount = maxNodeCount
            };
            if (!parallelBuild)
            {
                foreach (string projectPath in projectPaths)
                {
                    Console.WriteLine("Building {0}...", projectPath);
                    BuildResult buildResult = _buildManager.Build(buildParameters, CreateRequest(projectPath, target));
                    Console.WriteLine("=====> [{0}] {1}", buildResult.OverallResult, projectPath);
                }
            }
            else
            {
                _buildManager.BeginBuild(buildParameters);
                using (CountdownEvent countdownEvent = new CountdownEvent(projectPaths.Length))
                {
                    foreach (string projectPath in projectPaths)
                    {
                        Console.WriteLine("Building {0} in parallel...", projectPath);
                        BuildSubmission submission = _buildManager.PendBuildRequest(CreateRequest(projectPath, target));
                        submission.ExecuteAsync(o => {
                            Console.WriteLine("=====> [{0}] {1}", o.BuildResult.OverallResult, projectPath);
                            countdownEvent.Signal();
                        }, null);
                    }
                    countdownEvent.Wait();
                }
                _buildManager.EndBuild();
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                var targetAssembly = Path.Combine(GetMSBuildPath(), new AssemblyName(eventArgs.Name).Name + ".dll");
                return File.Exists(targetAssembly) ? Assembly.LoadFrom(targetAssembly) : null;
            };

            Builder builder = new Builder(Directory.GetCurrentDirectory());

            builder.Build(new [] { "resources/getvariables.proj" }, "Run", false);
        }

        private static string GetMSBuildPath()
        {
            var vsinstalldir = GetVSPath();
            var msBuildPath = Path.Combine(vsinstalldir, "MSBuild", "15.0", "Bin");
            return Environment.Is64BitProcess ? Path.Combine(msBuildPath, "amd64") : msBuildPath;
        }

        private static string GetVSPath()
        {
            // Dev console, probably the best case
            var vsinstalldir = Environment.GetEnvironmentVariable("VSINSTALLDIR");
            if (!string.IsNullOrEmpty(vsinstalldir))
            {
                Console.WriteLine($"Found VS from VSINSTALLDIR (Dev Console): {vsinstalldir}");
                return vsinstalldir;
            }

            var instances = VisualStudioLocationHelper.GetInstances();

            if (instances.Count == 0)
            {
                throw new Exception("Couldn't find Visual Studio");
            }

            Console.WriteLine($"Found VS from Setup API: {instances[0].Path}");
            if (instances.Count > 1)
            {
                Console.WriteLine($"WARNING: Found ${instances.Count} instances of VS! Picking the first...");
            }

            return instances[0].Path;
        }


    }
}
