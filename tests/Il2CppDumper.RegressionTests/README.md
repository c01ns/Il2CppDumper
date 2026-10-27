Run the binary format regression checks with .NET 8:

```powershell
dotnet run --project tests/Il2CppDumper.RegressionTests -c Release
```

The fixtures cover packed v108 RGCTX entries, 1/2/4-byte generic method and invoker indices (with and without adjustor thunks), invalid section boundaries/counts, v39 assembly records with optional module tokens, and version-dependent index decoding. They contain no game data and need no test-framework packages.

An optional path to the original v108 “百万金币” metadata enables sample-specific checks:

```powershell
dotnet run --project tests/Il2CppDumper.RegressionTests -c Release -- "path/to/global-metadata.dat"
```

Empty-header checks for other versions only exercise section handling; they do not establish full game compatibility for those versions.
