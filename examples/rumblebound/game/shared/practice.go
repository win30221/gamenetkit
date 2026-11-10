package shared

import "strings"

type PracticeBehavior uint8

const (
	PracticePatrol PracticeBehavior = 1 << iota
	PracticeCapture
	PracticeAttack
	PracticeDodge
	PracticeKeepDistance
)

func PracticeBehaviors(mode string) (PracticeBehavior, bool) {
	if mode == "kite" {
		return PracticeKeepDistance, true
	}
	if strings.HasSuffix(mode, "+kite") {
		base := strings.TrimSuffix(mode, "+kite")
		if base == "idle" {
			return 0, false
		}
		behavior, ok := practiceBaseBehaviors(base)
		if !ok {
			return 0, false
		}
		return behavior | PracticeKeepDistance, true
	}
	return practiceBaseBehaviors(mode)
}

func practiceBaseBehaviors(mode string) (PracticeBehavior, bool) {
	switch mode {
	case "idle":
		return 0, true
	case "move":
		return PracticePatrol, true
	case "attack":
		return PracticeAttack, true
	case "dodge":
		return PracticeDodge, true
	case "attack+dodge":
		return PracticeAttack | PracticeDodge, true
	case "move+attack":
		return PracticePatrol | PracticeAttack, true
	case "move+dodge":
		return PracticePatrol | PracticeDodge, true
	case "move+attack+dodge":
		return PracticePatrol | PracticeAttack | PracticeDodge, true
	case "capture":
		return PracticeCapture, true
	case "capture+attack":
		return PracticeCapture | PracticeAttack, true
	case "capture+dodge":
		return PracticeCapture | PracticeDodge, true
	case "capture+attack+dodge":
		return PracticeCapture | PracticeAttack | PracticeDodge, true
	default:
		return 0, false
	}
}

// IsPracticeMode is the shared HTTP/simulation allowlist. Modes are exact wire
// values so an unrecognized spelling cannot silently enable attacking behavior.
func IsPracticeMode(mode string) bool {
	_, ok := PracticeBehaviors(mode)
	return ok
}
