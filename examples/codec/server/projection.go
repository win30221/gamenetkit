package main

import (
	"fmt"
	"github.com/win30221/gamenetkit/server/interest"
	"github.com/win30221/gamenetkit/server/netcore"
)

const exitMargin = .75
const landmarkCount = 13
const maxEntities = 8 + landmarkCount + 32

type interestState struct {
	Mode         string  `json:"mode"`
	Radius       float64 `json:"radius"`
	Margin       float64 `json:"margin"`
	CenterX      float64 `json:"centerX"`
	CenterZ      float64 `json:"centerZ"`
	TotalPlayers int     `json:"totalPlayers"`
	TotalObjects int     `json:"totalObjects"`
	OwnedObjects int     `json:"ownedObjects"`
}

func landmarks() []objectState {
	out := make([]objectState, 0, landmarkCount)
	for _, z := range []float64{-5, 0, 5} {
		for _, x := range []float64{-6, -2, 2, 6} {
			out = append(out, objectState{ID: fmt.Sprintf("landmark-%d", len(out)+1), Life: 1, X: x, Z: z, Kind: "landmark"})
		}
	}
	return append(out, objectState{ID: "landmark-hub", Life: 1, X: 0, Z: 7, Kind: "global"})
}
func playerKey(id uint64) string { return fmt.Sprintf("player:%d", id) }

// Only the outbound projection changes. All recipients share one authority,
// while each SDK selector owns its own hysteresis history.
func (w *world) project(p *player, kind string) stateMessage {
	players, objects := w.roster(), w.objectRoster()
	if p.radius == 0 {
		p.radius = 3
	}
	if p.interestMode == "" {
		p.interestMode = "all"
	}
	owned := 0
	for _, o := range objects {
		if o.Owner == p.state.ID {
			owned++
		}
	}
	summary := interestState{Mode: p.interestMode, Radius: p.radius, Margin: exitMargin, CenterX: p.state.X, CenterZ: p.state.Z, TotalPlayers: len(players), TotalObjects: len(objects), OwnedObjects: owned}
	if p.interestMode == "aoi" {
		if p.selector == nil {
			p.selector = interest.New(p.radius, exitMargin, maxEntities)
		}
		candidates := make([]netcore.InterestObject, 0, len(players)+len(objects))
		for _, player := range players {
			candidates = append(candidates, netcore.InterestObject{ID: playerKey(player.ID), X: player.X, Z: player.Z, Always: player.ID == p.state.ID})
		}
		for _, object := range objects {
			candidates = append(candidates, netcore.InterestObject{ID: object.ID, X: object.X, Z: object.Z, Always: object.Kind == "global"})
		}
		selected := p.selector.Select(p.state.X, p.state.Z, candidates)
		receivedPlayers := make([]playerState, 0, len(players))
		receivedObjects := make([]objectState, 0, len(objects))
		for _, player := range players {
			if selected[playerKey(player.ID)] {
				receivedPlayers = append(receivedPlayers, player)
			}
		}
		for _, object := range objects {
			if selected[object.ID] {
				receivedObjects = append(receivedObjects, object)
			}
		}
		players, objects = receivedPlayers, receivedObjects
	}
	message := stateMessage{Type: kind, Protocol: 1, Tick: w.tick, Generation: 1, Players: players, Objects: objects, Interest: summary}
	if kind == "welcome" {
		message.PlayerID = p.state.ID
		message.TickRate = tickRate
		message.SnapshotRate = snapshotRate
	}
	return message
}
