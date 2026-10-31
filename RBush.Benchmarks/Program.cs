using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(RBush.Benchmarks.RBushBenchmarks).Assembly).Run(args);
