package gateway

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

// Map is configured once, before registering routes or admitting any matches.
func (s *Server) arenaMap() *shared.ArenaMapDefinition {
	if s.Map != nil {
		return s.Map.Clone()
	}
	return shared.ArenaMap()
}
func (s *Server) mapJSON() string {
	if s.Map == nil {
		return shared.ArenaMapJSON()
	}
	b, _ := json.Marshal(s.Map)
	return string(b)
}
func (s *Server) mapHash() string {
	h := sha256.Sum256([]byte(s.mapJSON()))
	return hex.EncodeToString(h[:])
}
func (s *Server) rules() shared.Config {
	return shared.RulesForMap(s.Map)
}
