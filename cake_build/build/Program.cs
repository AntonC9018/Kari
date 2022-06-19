using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cake.Cli;
using Cake.Common.Modules;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Frosting;

namespace Kari.Build
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var host = new CakeHost();
            host.ConfigureServices(a =>
            {
            });
            return host.Run(args);
        }
    }

    public class BuildContext : FrostingContext
    {
        public bool Delay { get; set; }

        public BuildContext(ICakeContext context)
            : base(context)
        {
            Delay = context.Arguments.HasArgument("delay");
        }
    }

    public class BuildGeneratorTask : FrostingTask<BuildContext>
    {
    } 


    [TaskName("Hello")]
    public sealed class HelloTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            context.Log.Information("Hello");
        }
    }

    [TaskName("World")]
    [IsDependentOn(typeof(HelloTask))]
    public sealed class WorldTask : AsyncFrostingTask<BuildContext>
    {
        // Tasks can be asynchronous
        public override async Task RunAsync(BuildContext context)
        {
            if (context.Delay)
            {
                context.Log.Information("Waiting...");
                await Task.Delay(1500);
            }

            context.Log.Information("World");
        }
    }

    [TaskName("Default")]
    [IsDependentOn(typeof(WorldTask))]
    public class DefaultTask : FrostingTask
    {
    }
}