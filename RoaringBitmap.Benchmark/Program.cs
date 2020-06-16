using System.Linq;
using BenchmarkDotNet.Running;
using RoaringBitmap.Benchmark.MicroBenchmarks;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Toolchains.CsProj;

namespace RoaringBitmap.Benchmark
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            //var types = typeof(MicroBenchmark).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(MicroBenchmark).IsAssignableFrom(t)).ToList();
            var types = new[] { typeof(MicroBenchmarkCensus1881) };

            var cfg = DefaultConfig.Instance // A configuration for our benchmarks
                .AddJob(Job.Default // Adding first job
                        .AsBaseline()
                        .WithRuntime(CoreRuntime.Core31) // 
                        .WithPlatform(Platform.X64) // Run as x64 application
                        .WithOutlierMode(Perfolizer.Mathematics.OutlierDetection.OutlierMode.RemoveAll)
                        .WithStrategy(BenchmarkDotNet.Engines.RunStrategy.Throughput)
                        .WithMinWarmupCount(1).WithMaxWarmupCount(3)
                        .WithMinIterationCount(10).WithMaxIterationCount(20)
                );

            //BenchmarkRunner.Run
            //var bis = new BenchmarkRunInfo[] {
            //    new BenchmarkRunInfo { Config = cfg, Type = types[0] },
            //    new BenchmarkRunInfo { Config = cfg2, Type = types2[0] }
            //};
            foreach(var type in types)
                BenchmarkRunner.Run(type, cfg);
        }
    }

    public class BenchmarkConfig : ManualConfig {

        public BenchmarkConfig() {
            //Add(Job.Default.With(Runtime.Core).With(CsProjCoreToolchain.NetCoreApp31));
        }
    }
}