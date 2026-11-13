# Tower Duel protocol 3

Go owns rooms, readiness, energy, hands, deployment, navigation, effects, damage
and results. Unity sends intent and renders authoritative snapshots through
GameNetKit transport and interpolation. Animation and projectiles do not calculate damage.

- WebSocket `/ws`: strict flat JSON; 1 KiB input, 32 KiB output.
- Simulation 30 Hz; complete recipient-specific snapshots 15 Hz.
- At most 32 two-seat rooms, 64 connections, 80 living soldiers and 16 effects per room.
- `map.json` and `rules.json` are canonical; SHA-256 of their raw bytes becomes
  `mapHash` and `rulesHash`. Rules schema 3, revision `eight-cards-1`.
- Each recipient receives only their own energy, four-card hand, next card,
  hand version and action watermark. Opponent hands and queues are never projected.

## Joining and restoring

Every hello field is required, including the empty token for create/join:

```json
{"type":"hello","protocol":3,"mode":"create","room":"","name":"Player","mapHash":"<sha256>","rulesHash":"<sha256>","token":""}
```

Use `mode: "join"` with the six-character room code. A `welcome` contains room,
match, seat, hashes, the complete snapshot and a private 43-character `resumeToken`.
Subsequent snapshots omit the token. The connection binds to its seat; input has no player ID.

After a transport disconnect, the simulation continues and the seat is reserved
for 30 seconds of engine ticks. Reconnect with `mode: "resume"`, the same room,
hashes and `token` from welcome. Wrong, expired and already-connected tokens are
rejected. A successful welcome restores the current match and complete state;
the client continues action IDs above `actionWatermark`. The client retries an
unconfirmed action using its original ID. The token is a bearer credential and
must not be logged, shared with opponents or put in public snapshots.

Expiry forfeits an active match; both expired seats release the room. An explicit
`leave` invalidates the credential immediately. Restoration is process-local;
server restarts do not persist rooms.

```json
{"type":"ready","match":"<match>"}
{"type":"deploy","match":"<match>","action":1,"card":"guard","x":-5,"z":-3.2}
{"type":"deploy","match":"<match>","action":2,"card":"fireball","x":-5,"z":7.7}
{"type":"rematch","match":"<match>"}
{"type":"leave","match":"<match>"}
```

## Cards and cycling

The catalog order defines the symmetric starting deck. The first four cards are
in hand; the remaining four form a queue. Successful play replaces that hand slot
with the queue head and appends the played card to the queue. Four further plays
return the card to hand. Failure, cancellation and retries do not cycle or charge again.

| ID | Cost | Count | Role |
| --- | --- | --- | --- |
| guard | 3 | 1 | Durable melee |
| archer | 3 | 1 | Ranged single target |
| swords | 3 | 3 | Fragile melee squad |
| bomber | 4 | 1 | 1.5 m splash around the attack target |
| charger | 4 | 1 | 2× hit after 2.5 m of movement |
| siege | 5 | 1 | Attacks buildings only |
| fireball | 4 | 0 | 2.2 m radius, 110 damage, 45% tower damage |
| frost | 2 | 0 | 2.8 m radius, 55% movement/attack slow, 120 ticks |

All numerical parameters are in `rules.json`; art does not determine gameplay.
Troops have a 0.32 m radius. Swords use an oriented three-member formation. Every
member must fit the player's bank, avoid living buildings and fit the unit cap.
Soldier overlap is allowed at deployment; authoritative circle collision separates
and pushes bodies afterwards. River, buildings and boundaries remain obstacles.
An accepted squad pays once; invalid placement rejects the whole squad.

Spells can target a centre inside the full battlefield, including the enemy bank.
They affect enemies only. Effect snapshots include `id`, `kind`, `seat`, `x`, `z`,
`radius`, `start`, `impact` and `end`. Fireball hits once at its scheduled impact
tick, after a 24-tick delay from the next simulation step; the visual remains for
18 ticks. Frost repeatedly applies the slow within its active area to troops and
towers; overlapping slows use the strongest value rather than multiplying.
Active effects stop on match end and are cleared for rematches.

Deployment, energy deduction, effect/unit creation, cycling and the cached action
result are one authoritative operation. An `action` response contains `match`,
`action`, `accepted`, `reason`, `unitID`, `count`, `hand`, `nextCard`, `handVersion`
and `energy`. For spells, `count` is zero. Each seat caches 128 results; retries
return exactly the cached result, including failures. Evicted action IDs are
rejected. IDs are positive, monotonically increasing and within JavaScript's safe
integer range. A client must not overwrite a newer hand version with a delayed ACK.

## Combat and match flow

Both players ready triggers a two-second countdown. Energy starts at five, caps
at ten and restores one per three seconds, doubled in the last minute and overtime.
Destroying the enemy keep ends the match. At three minutes, destroyed front towers
determine the winner; ties enter sixty seconds of sudden death. Simultaneous keep
destruction, or simultaneous tower destruction during overtime, draws.

The deployment centre chooses the nearest bridge for the formation. Building
objectives follow that lane's front tower, then the keep. Before the first attack,
eligible nearby troops take priority; an attacker then keeps its living target
until death. Siege ignores troop targets. Towers also retain living targets outside
range and wait for their return. `target` is current intent; optional `lockedTarget`
is the attack lock. `attackTick` may refer to an earlier target that has since died.

Front towers start `active`; keeps activate permanently after taking damage or
losing either friendly front tower. `slowed` and `charging` drive presentation.
Same-tick damage is accumulated before results are decided.

After an ended match, both seats must confirm `rematch`. The same room and seats
receive a fresh match ID and welcome. Units, effects, towers, energy, hands, action
cache and readiness reset. Both players ready again. Old-match input and delayed
old-match responses cannot affect the new game.

## Transport and local serving

Unknown, duplicate, missing, null and case-variant fields are rejected, as are
protocol/hash mismatches. The limit is forty messages per second per connection.
Pending snapshots coalesce to the latest; confirmations have a separate queue.
These bounds do not guarantee delivery to a stalled peer; a failed connection can restore.

`GET /api/config` exposes protocol, hashes, catalog and rule limits, not player
secrets. `-web <directory>` optionally serves the built WebGL player with correct
Gzip and MIME headers; `/api`, `/ws` and `/healthz` retain their normal routes.
The development impairment proxy and 80-actor fixture are verification tools and
are not part of the production game protocol.
