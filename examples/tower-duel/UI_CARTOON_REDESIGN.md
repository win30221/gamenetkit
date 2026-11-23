# Tower Duel — Cartoon Fantasy UI Overhaul

Branch: `feat/tower-duel-cartoon-ui-overhaul`

## Target look
The UI uses a cohesive, bold-outlined, scroll-and-emblem cartoon fantasy look:
- Near-black outer outlines and warm gold highlights
- Cream parchment panels, orange-brown scroll caps and rivets
- Glossy grass-green primary buttons and blue secondary buttons
- Deep indigo HUD frames and readable violet elixir markers
- Existing B3 card portraits and battlefield art remain intact

## Live screens and coverage

| Screen | Current entrypoint | Restyle |
| --- | --- | --- |
| Main lobby / quick match | `Duel.uxml`: `battlePage` | Scroll title, framed battlefield preview, match button, input fields and tab bar |
| Friend room | `friendRoom` | Parchment frame, blue Join action, create and room-code entry |
| Deck builder | `deckPage` and dynamically generated `CardTile` | Card frames, counters, reserve hand, catalog, selection, tabs |
| Match HUD | `topHud`, `dock` | Framed clock/controls, violet energy meter, card hand, next card and notice |
| Waiting and countdown | `waitingLayer` | Scroll-like panel and green Ready button |
| Results / rematch | `resultLayer` | Parchment result dialog and call-to-action |
| Card drag ghost | `dragPreview` | Same custom frame as active card |
| Tower and unit health | `healthLayer` | High-contrast blue/red health bars and outlined text |

There are **no shop, missions, payment or account screens** in the current
`Duel.uxml`. Concept images of such screens do not represent existing features.

## Implementation

- `DuelCartoonSkin.cs` generates nine-slice-capable textures at runtime. It
  applies the same visual language to static UXML elements and dynamic cards.
- `Duel.uss` contains the cartoon theme and retains existing layout dimensions.
- `DuelHud.cs` applies the skin after card and menu creation.
- `DuelMenu.cs` updates selected-tab artwork on each tab change.
- `Duel.uxml` is unchanged to preserve element IDs and all event wiring.
- No rules, deployment, collision, targeting, card costs, camera, server or
  network code is changed.

## Unity acceptance checklist (manual)

1. Open `examples/tower-duel/client` in the pinned Unity editor. Check Console
   for C# and USS import errors, then enter Play Mode.
2. On a narrow mobile preview, check that the quick-match button, name entry,
   room creation / joining and deck tabs can all be used.
3. Switch battle/deck repeatedly. Confirm the selected tab is green, and that
   all dynamic cards and their costs remain legible.
4. Enter a match. Confirm units spawn only from legal deployment zones; dragging
   and cancellation still work; the dark HUD does not obscure the arena.
5. Test enough / insufficient energy, active selection and the next-card area.
6. Exercise waiting, countdown, victory, defeat and rematch flows.
7. Test WebGL build, portrait layout, wide viewport and mobile safe-area inset.
8. Compare HUD and button hitboxes to the original UI before merging.

## Limitations / next art pass

This pass changes **UI presentation**, not all in-world art. Existing battle
environment, units, their sprite atlases and the lobby illustration stay as-is.
The generated UI textures are intentionally lightweight source-controlled C#,
rather than committed PNG atlases; for final polish, replace individual surfaces
with production 9-slice sprites while keeping class and element names stable.

Unity Editor, WebGL compilation and screenshot verification were not available
through this editing environment; these remain required before merging to main.
