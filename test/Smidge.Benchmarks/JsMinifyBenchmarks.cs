﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Diagnostics.Windows;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Microsoft.AspNetCore.NodeServices;
using Microsoft.Extensions.Options;
using Moq;
using NUglify.Css;
using Smidge.CompositeFiles;
using Smidge.FileProcessors;
using Smidge.JavaScriptServices;
using Smidge.Models;
using Smidge.Nuglify;
using Smidge.Options;

namespace Smidge.Benchmarks
{
    [Config(typeof(Config))]
    public class JsMinifyBenchmarks
    {
        /// <summary>
        /// initialize one time
        /// </summary>
        static JsMinifyBenchmarks()
        {            
            AssemblyPath = Path.Combine(Path.GetDirectoryName(typeof(JsMinifyBenchmarks).Assembly.Location), "temp");
            Directory.CreateDirectory(AssemblyPath);

            if (!Directory.Exists(Path.Combine(AssemblyPath, "node_modules")))
            {
                //copy over all of the node modules
                var dirInfo = new DirectoryInfo(AssemblyPath);
                while (dirInfo != null && !dirInfo.Name.Equals("test", StringComparison.OrdinalIgnoreCase))
                {
                    dirInfo = dirInfo.Parent;
                }
                dirInfo = dirInfo.Parent; //this will be the sln root
                dirInfo = dirInfo.GetDirectories("src").Single();
                dirInfo = dirInfo.GetDirectories("Smidge.Web").Single();
                dirInfo = dirInfo.GetDirectories("node_modules").Single();

                DirectoryCopy(dirInfo.FullName, Path.Combine(AssemblyPath, "node_modules"), true);
            }

            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Smidge.Benchmarks.jQuery.js")))
            {
                JQuery = reader.ReadToEnd();
            }
        }

        private class Config : ManualConfig
        {
            public Config()
            {
                Add(new MemoryDiagnoser());
                Add(new MinifiedPercentColumn());

                //The 'quick and dirty' settings, so it runs a little quicker
                // see benchmarkdotnet FAQ
                Add(Job.Default
                    .WithLaunchCount(1) // benchmark process will be launched only once
                    .WithIterationTime(100) // 100ms per iteration
                    .WithWarmupCount(3) // 3 warmup iteration
                    .WithTargetCount(3)); // 3 target iteration           

            }
        }

        private JsMinifier _jsMin;
        private NuglifyJs _nuglify;
        private UglifyNodeMinifier _jsUglify;        
        private static readonly string AssemblyPath;

        public static readonly string JQuery;

        private class NullServiceProvider : IServiceProvider
        {
            public object GetService(Type serviceType)
            {
                return null;
            }
        }

        [Setup]
        public void Setup()
        {
            var websiteInfo = new Mock<IWebsiteInfo>();
            websiteInfo.Setup(x => x.GetBasePath()).Returns("/");
            websiteInfo.Setup(x => x.GetBaseUrl()).Returns(new Uri("http://test.com"));

            _jsMin = new JsMinifier();
            _nuglify = new NuglifyJs(
                new NuglifySettings(new NuglifyCodeSettings(null), new CssSettings()),
                Mock.Of<ISourceMapDeclaration>());
            
            var nodeServices = new SmidgeJavaScriptServices(NodeServicesFactory.CreateNodeServices(
                new NodeServicesOptions(new NullServiceProvider())
                {
                    ProjectPath = AssemblyPath,
                    WatchFileExtensions = new string[] {}
                }));
            _jsUglify = new UglifyNodeMinifier(nodeServices);            
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, true);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs);
                }
            }
        }

        public async Task<string> GetJsMin()
        {
            using (var bc = BundleContext.CreateEmpty())
            {
                var fileProcessContext = new FileProcessContext(JQuery, new JavaScriptFile(), bc);
                await _jsMin.ProcessAsync(fileProcessContext, s => Task.FromResult(0));
                return fileProcessContext.FileContent;
            }
        }

        public async Task<string> GetNuglify()
        {
            using (var bc = BundleContext.CreateEmpty())
            {
                var fileProcessContext = new FileProcessContext(JQuery, new JavaScriptFile(), bc);
                await _nuglify.ProcessAsync(fileProcessContext, s => Task.FromResult(0));
                return fileProcessContext.FileContent;
            }
            
        }

        public async Task<string> GetJsServicesUglify()
        {
            using (var bc = BundleContext.CreateEmpty())
            {
                var fileProcessContext = new FileProcessContext(JQuery, new JavaScriptFile(), bc);
                await _jsUglify.ProcessAsync(fileProcessContext, s => Task.FromResult(0));
                return fileProcessContext.FileContent;
            }
        }

        [Benchmark(Baseline = true)]
        public async Task JsMin()
        {
            await GetJsMin();
        }

        [Benchmark]
        public async Task Nuglify()
        {
            await GetNuglify();
        }

        [Benchmark]
        public async Task JsServicesUglify()
        {
            await GetJsServicesUglify();
        }


    }
}