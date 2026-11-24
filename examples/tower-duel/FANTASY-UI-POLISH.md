# Fantasy B3 polish — character detail preview

This revision replaces the flat geometric character backdrop with a painted stage
asset slot, supports independently sized compact button textures, and gives
troops a UI-only animated idle presentation.

- Ground: the existing single front-facing Idle sprite is deformed locally
  above its planted feet. It is not a newly authored multi-frame atlas.
- Flight: the front-facing six-frame flying cycle is played in place, with a hover.
- Griffin: the detail panel can toggle the ground and airborne forms.
- Spells: keep the original card illustration, without fabricating a unit sprite.
- Closing the dialog or leaving the lobby releases the preview RenderTexture.
- Card rules, match logic, and battle sprite atlases are unchanged.

## Static artwork

The UI loads artwork from Resources/TowerDuel/B3/FantasyUI and
Resources/TowerDuel/B3/CharacterStages. The full-resolution binary art is delivered
as a separate git binary patch, `tower-duel-fantasy-ui-art.patch`. Apply once at
the repository root. Without the art patch the UI uses the original skin fallback
and the existing B3 menu battlefield illustration. It will not have the approved
crest, painted stage or all of the new icons.

## Validation

In Unity Play Mode, open troop and spell details. Check that the portrait animates
continuously without changing direction, that the griffin form toggle works, that
the modal scrolls on narrow screens and that repeated open/close operations do
not leave preview render targets allocated. Confirm standalone and WebGL builds.
