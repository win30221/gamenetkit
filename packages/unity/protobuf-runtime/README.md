# Google.Protobuf runtime

This local UPM package contains the unmodified official Google.Protobuf **3.36.2**
`lib/netstandard2.0/Google.Protobuf.dll` and upstream BSD license. Use Unity's
**.NET Standard 2.1** API compatibility profile (targeted with the repository's
pinned Unity version; see the Protobuf example validation report). Unity supplies System.Memory and
System.Runtime.CompilerServices.Unsafe; do not install duplicate framework DLLs.
No NuGet Editor plugin is required.

Maintainers restore with `pwsh ./tools/Restore-Protobuf.ps1`; `-Check` verifies the
committed DLL and license against `protobuf-toolchain.json`. Official NuGet and
compiler downloads stay under ignored `.cache/protobuf`; only the small runtime
DLL is distributed. Tools and runtime use matching release 36.2; Go independently
uses official `google.golang.org/protobuf` v1.36.11.

Applications reference generated message parsers directly. IL2CPP/WebGL build and
execution are separate compatibility gates; managed compilation alone is not a
claim of AOT or browser compatibility. See the Protobuf example for smoke checks.
