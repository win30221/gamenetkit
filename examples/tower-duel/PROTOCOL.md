# Tower Duel protocol 3

Go owns rooms, readiness, energy, hands, deployment, navigation, effects, damage
and results. Unity sends intent and renders authoritative snapshots through
GameNetKit transport and interpolation. Animation and projectiles do not calculate damage.

- WebSocket `/ws`: strict flat JSON; 1 KiB input, 32 KiB output.
- Simulation 30 Hz; complete recipient-specific snapshots 15 Hz.
- At most 32 two-seat rooms, 64 connections, 80 living soldiers and 16 effects per room.
- `client/Assets/Resources/TowerDuel/map.json` and `rules.json` are canonical; SHA-256 of their raw bytes becomes
  `mapHash` and `rulesHash`. Rules schema 3, revision `air-combat-1`.
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

The first eight catalog entries define the default deck. A new `create`, `join`,
or `quick` hello can include `deck`, an ordered array of eight distinct catalog
IDs. Go validates the deck before assigning a seat. For each seat, the first four
cards are in hand and the remaining four form a queue. Successful play replaces that hand slot
with the queue head and appends the played card to the queue. Four further plays
return the card to hand. Failure, cancellation and retries do not cycle or charge again.

In `quick` mode, Go responds with `{"type":"queued"}` immediately and every two
seconds until a second player arrives. Closing the connection removes the waiter.
Each player then receives their own `welcome` and the usual countdown begins.
Quick matches include `"quick":true` in match state. Rematches deal from each
seat's selected deck again. A `resume` hello uses the original token and restores
the current hand and queue.

| ID | Cost | Count | Role |
| --- | --- | --- | --- |
| guard | 3 | 1 | Durable melee |
| archer | 3 | 1 | Ranged single target, ground and air |
| swords | 3 | 3 | Fragile melee squad |
| bomber | 4 | 1 | 1.5 m ground splash around the attack target |
| charger | 4 | 1 | 2× hit after 2.5 m of movement |
| siege | 5 | 1 | Attacks buildings only |
| fireball | 4 | 0 | Meteor, 2.2 grid-unit radius, 110 damage on all targets |
| frost | 2 | 0 | 2.8 grid-unit radius, 55% movement/attack progress, 120 ticks |
| falcon | 3 | 1 | Flying assault unit |
| spearman | 3 | 1 | Extended-reach ground melee |
| griffin | 4 | 1 | Flies after 300 ticks and doubles current and maximum HP |
| standard | 4 | 1 | Nearby allies and self gain movement speed, strongest aura applies |

All numerical parameters are in `rules.json`; art does not determine gameplay.
Each troop defines its collision radius and weight. Swords use an oriented three-member formation. Every
member must fit the player's bank, avoid living buildings and fit the unit cap.
Soldier overlap is allowed at deployment; authoritative circle collision separates
and pushes bodies afterwards. River, buildings and boundaries remain obstacles.
An accepted squad pays once; invalid placement rejects the whole squad.

Every troop has its own `deployTicks` (currently 30 ticks / one second for all six
cards, configurable from 0 to 300). An accepted deployment immediately creates
the full squad, charges energy and cycles the hand. Each member carries the same
absolute `readyTick = deployment tick + deployTicks`; zero means immediately
ready. Before that tick the troop cannot select a target, move, build charge or
start an attack/windup. At `readyTick` it can act normally; its first hit still
requires the normal attack windup. Deploying troops remain targetable, damageable
and subject to collision push. Frost does not extend deployment time.

`readyTick` is replicated to both seats and retained across retries and recovery.
The client displays the remaining deployment time against the shared render
timeline and keeps the troop idle until that timeline reaches readiness. A
stalled stream cannot complete deployment using local wall time. Spells retain
their own `delayTicks` and do not use troop deployment time.

Spells can target a centre inside the full battlefield, including the enemy bank.
They affect enemies only. Effect snapshots include `id`, `kind`, `seat`, `x`, `z`,
`radius`, `start`, `impact` and `end`. Fireball hits once at its scheduled impact
tick, after a 34-tick delay from the next simulation step; the visual remains for
18 ticks. Frost repeatedly applies the slow within its active area to troops and
towers; overlapping slows use the strongest value rather than multiplying.
Spells use horizontal circle overlap for ground troops, flying troops and buildings.
The meteor drops vertically at its selected destination. At impact, units strictly below
the configured weight threshold are pushed outward up to one grid unit and suspend
movement and attacks for nine ticks. Terrain can stop displacement while the pause continues.
Resistance to knockback leaves damage and current action progress intact.
Frost recalculates after movement and pushes. The client samples the same footprint
rule along its render timeline.
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
at ten and restores one per three seconds throughout normal time. Only overtime
doubles recovery; the transition tick still uses the normal rate.
Destroying the enemy keep ends the match. At three minutes, destroyed front towers
determine the winner; ties enter sixty seconds of sudden death. Simultaneous keep
destruction, or simultaneous tower destruction during overtime, draws.

The deployment centre chooses the nearest bridge for the formation. Building
objectives follow that lane's front tower, then the keep. Troops select the nearest
eligible enemy soldier inside their centre-based aggro radius, measured by collision
edge distance. If no soldier is eligible, they advance toward a building. Attack
windup starts a lock. Troop locks last while the target is alive, in aggro range
and attackable. Building locks last until the building falls. Siege targets buildings.
Real wall segments block ground attack lines and movement, while ground-air combat can
cross the wall. Both towers target air and ground troops in range across the wall.
The map has a one-grid-thick, two-grid-high central wall with two three-grid gates.
Side walls follow the exterior map boundary. Wall concealment is visual; the server
publishes all units. Unit snapshots include optional airborne, transformTick and pausedUntil.
Towers also retain living targets outside
range and wait for their return. `target` is current intent; optional `lockedTarget`
is the attack lock. For troops, `attackTick` starts the attack animation and
windup, while `hitTick` and `hitTarget` record the most recent completed attack.
These event ticks may refer to an earlier target that has since died; current
intent must not redirect a completed shot's projectile. Tower `attackTick` still
records its shot directly.

Every troop has a positive `windupTicks` in `rules.json`: guard 11, archer 17,
swords 8, bomber 21, charger 13 and siege 17 (30 ticks per second). Starting a
windup deals no damage. Only a living soldier that finishes its windup against
the same living target in range adds damage, splash or a charged hit. Death
cancels the unfinished attack; target death or leaving range discards its
progress. A replacement target needs a fresh windup. Frost slows both windup
and cooldown progress. Cooldown runs from attack start and includes windup,
preserving the configured interval between attacks. Ranged projectiles launch
only after an authoritative `hitTick`, rather than at animation start.

Tower shots use `towerCooldownTicks` (38 ticks, about 1.27 seconds).
Front towers start `active`; keeps activate permanently after taking damage or
losing either friendly front tower. `slowed` reports status at the published foot
position; frost presentation samples the render-time position. `charging` drives
charge presentation.
Attacks whose windups finish on the same tick resolve together, including mutual
lethal hits. A soldier killed on an earlier tick cannot complete its windup.
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
