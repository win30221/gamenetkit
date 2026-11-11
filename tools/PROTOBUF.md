# Official Protobuf tools

From the repository root, with PowerShell 7 on Windows x64:

```powershell
pwsh ./tools/Generate-Protocol.ps1 -Config ./examples/protobuf/protocol.json
pwsh ./tools/Generate-Protocol.ps1 -Config ./examples/protobuf/protocol.json -Check
pwsh ./tools/Restore-Protobuf.ps1 -Check
```

Generation uses only official `protoc` and `protoc-gen-go`. The first invocation
downloads the locked compiler and installs the official Go generator with the
repository's pinned Go compiler. A working network connection is required once;
subsequent generation uses ignored workspace caches and never uploads schemas.
`protobuf-toolchain.json` locks compiler/runtime versions, SHA256 archive and DLL
hashes, and the Go module checksum. Go's checksum database verifies plugin source.
The runtime DLL is committed so example consumers need no generation tools.

The JSON config paths are relative to the config file: `schemaRoot` is the proto
include root, `inputs` lists proto paths relative to that root, and `goOutput` /
`csharpOutput` are output directories. Language namespaces stay in the `.proto`.
Absolute output paths and paths outside the config directory are supported; they
are explicit destinations in the caller's configuration. Use dedicated output
directories. Drive roots, the config directory, the schema root and its ancestors
are rejected. Inputs must be relative `.proto` files within the schema root.
Imported well-known types are not implicitly exposed; add their schema sources to
your schema root if your application uses them.

`-Check` generates in a unique temporary cache directory and compares exact file
hashes. It does not write output or alter Unity metadata. Normal generation only
copies generated source files and never deletes output directories or `.meta`
files. If a schema removal leaves obsolete generated source, both modes report it
and require explicitly removing that file after checking references. Keep a
dedicated generated directory per config.

`Restore-Protobuf.ps1` reconstructs the local runtime UPM package from the locked
official NuGet package and license; it preserves existing Unity metadata. See
`packages/unity/protobuf-runtime/README.md` for framework dependency requirements.

Version rationale: the [official support table](https://protobuf.dev/support/version-support/)
lists protoc 36.x / C# 3.36.x as active and Go 1.36.x as active. Compiler 36.2 and
C# 3.36.2 match the upstream release; Go 1.36.11 uses its independent versioning.
The [cross-version guarantee](https://protobuf.dev/support/cross-version-runtime-guarantee/)
and [official NuGet package](https://www.nuget.org/packages/Google.Protobuf/3.36.2)
are the compatibility and dependency references. Build/runtime validation remains
necessary for each Unity target.
