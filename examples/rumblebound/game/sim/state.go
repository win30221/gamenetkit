package sim

// PlayerState is authoritative simulation state. Wire-only fields (for example
// optional dash presentation metadata) are projected by snapshot.go, never
// stored back into the battle. These types intentionally have no JSON tags.
type PlayerState struct {
	BasicReadyTick, AttackReleaseTick  uint64
	AttackTargetID                     string
	AttackTargetLife, BasicActionID    uint64
	PlayerID, EntityID, TeamID, HeroID string
	X, Z                               float64
	FacingX, FacingZ                   float64
	Ack                                uint64
	Online, Left, Bot                  bool
	HP                                 int
	FireReadyTick, DashReadyTick       uint64
	AttackStopUntilTick                uint64
	RespawnTick, ProtectedUntilTick    uint64
}

type ProjectileState struct {
	ActionID            uint64
	ID, OwnerID, TeamID string
	X, Z, DX, DZ        float64
	ExpiresTick         uint64
}

type ProjectileEnd struct {
	ID, Kind, TargetID, OwnerID string
	X, Z                        float64
	Tick                        uint64
}

type CombatEvent struct {
	ActionID                    uint64
	ID, Kind, OwnerID, TargetID string
	X, Z, DX, DZ                float64
	Tick                        uint64
}

type BattleResult struct {
	Outcome, Reason, WinnerTeamID string
	EndedTick                     uint64
}

// InputIntent contains only the fields consumed by simulation. Admission,
// protocol versions, tickets and transport message types stay at the boundary.
type InputIntent struct {
	TargetID               string
	TargetLife             uint64
	ViewTick, ObservedTick uint64
	Generation             uint32
	Seq                    uint64
	X, Z, Echo             float64
	Action                 string
	ActionID, ActionTick   uint64
	AimX, AimZ             float64
	FacingX, FacingZ       float64
	HasGroundTarget        bool
	TargetX, TargetZ       float64
}
