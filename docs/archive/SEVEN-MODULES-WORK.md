# Seven-module implementation work log

> Historical record, archived 2026-10-31. Package counts, measurements and results below describe that implementation stage, not the current checkout. Current contracts: [MODULES](../MODULES.md). Test entry points: [VALIDATION](../VALIDATION.md). Later results: [first-version validation record](VALIDATION-2026-10-31.md).

Scope: all seven requested techniques, only under RealtimeFramework. Original game is a baseline.

1. Unity clock: snapshot fallback / disciplined monotonic server-tick estimate; snapshots and stamped probes feed one clock, actions consume it.
2. Unity replication: generic bounded full-roster store, immutable copy adapter, generation/tick guards and added/updated/removed lists. Actual actor reconciliation consumes it; direct snapshot path remains.
3. Unity actions: bounded speculative presentation ledger, authoritative action IDs, confirmation/rejection/timeout and replay suppression. Immediate attack animation/SFX/start effect only; HP/cooldowns/hits authoritative.
4. Go history: generic cloned bounded tick history, bracket queries. Record player collision poses each world step when requested; expose sample count.
5. Go lag compensation: off / bounded melee-target rewind, max 6 ticks at 30Hz. Current attacker and static cover, historical living targets of same life/connection, current HP/protection checks. Projectile flight and ground impact retain current-state rules. History required.
6. Go + Unity codec: JSON / acknowledged-baseline byte-copy/literal delta, bounded frame cache and payload decoder, missing baseline requests full recovery, reconnect resets. Independent optional position quantization in wire projection only. Never modify domain state.
7. Go interest: all / distance with hysteresis, always self/owned relevant objects, immediate lifecycle removal/re-entry, bounded membership. Does not change simulation/collision/score.

Negotiation: hello-only options, validated server capabilities and per-seat settings; classic clients unchanged. Frame ack travels in input. Config advertises supported server modules. Samples' only concrete references in module composition files (Go uses exclusion build tags for removal).

Validation required: standalone Go tests/vet; independent Unity SDK tests; shared Go/C# codec fixtures; real client lifecycle/action/weak-net/codec-loss/resync tests; existing UI/private/practice gates; Windows/WebGL builds; optional package removal with remaining modules; original 598-file hashes unchanged. Record actual results in VALIDATION.md. No claims of world rollback, physical packet loss simulation, perfect one-way clock knowledge, or general combat determinism.

Status: implementations, sample adapters, lifecycle/UI, and documentation completed. Standalone SDK and cross-language codec tests, Go tests/vet, 13 SDK browser tests, 38 diagnostic browser tests, native UI/private/practice/recovery gates, seven-feature weak-network scenario, and RTT conditioning scenario pass. Final seven-feature run: 76 encoded frames, 3 missing-baseline events, AOI exit/re-entry, immediate start without duplicate animation, rejection, verified historical target query, and identity reconnect. Native RTT: 2.59 ms baseline / 120.25 ms conditioned mean (three samples each). Unity individual and all-nine removal builds and Go individual/all-four physical removals pass. Metadata: 649 files, zero errors/warnings. Original baseline: all 598 hashes unchanged. Final Windows build completed; final WebGL build and HTTP checks recorded in VALIDATION.md and build logs. Updated demo backend runs only on framework ports 18090/18490; original 8080 remains available. No manual browser gameplay claim. Detailed evidence and limits: VALIDATION.md.
