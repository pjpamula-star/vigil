global using System.IO;

// Paths.Root is static, so test classes must not run at the same time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
