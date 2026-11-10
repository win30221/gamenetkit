package lobby

import (
	"errors"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

// CreatePractice reserves one server-owned opponent. The bot deliberately has
// no memberRoom entry, so it cannot authenticate lobby or ticket operations.
func (m *Manager) CreatePractice(user, heroID, botHeroID, mode string) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	if user == "" {
		return Snapshot{}, errors.New("invalid_user")
	}
	if _, exists := m.memberRoom[user]; exists {
		return Snapshot{}, errors.New("already_in_room")
	}
	if heroID == "" {
		heroID = "ranger"
	}
	if botHeroID == "" {
		botHeroID = "ranger"
	}
	if mode == "" {
		mode = "idle"
	}
	if _, ok := shared.Rules().Hero(heroID); !ok {
		return Snapshot{}, errors.New("invalid_hero")
	}
	if _, ok := shared.Rules().Hero(botHeroID); !ok {
		return Snapshot{}, errors.New("invalid_hero")
	}
	if !shared.IsPracticeMode(mode) {
		return Snapshot{}, errors.New("invalid_bot_mode")
	}
	if len(m.rooms) >= MaxRooms {
		return Snapshot{}, errors.New("room_capacity")
	}
	code := randomCode()
	for m.rooms[code] != nil {
		code = randomCode()
	}
	bot := "bot_" + randomMatchID()[2:]
	r := &room{code: code, host: user, teams: []Team{{ID: "t1", Capacity: 1}, {ID: "t2", Capacity: 1}},
		members:    map[string]*Member{user: {UserID: user, TeamID: "t1", HeroID: heroID, Ready: true}, bot: {UserID: bot, TeamID: "t2", HeroID: botHeroID, Ready: true, Bot: true}},
		lastActive: m.now(), practice: true, botMode: mode, presence: map[string]time.Time{user: m.now()}}
	m.rooms[code], m.memberRoom[user] = r, code
	return snapshot(r), nil
}

func (m *Manager) removeRoom(r *room) {
	for user := range r.members {
		delete(m.memberRoom, user)
	}
	if r.matchID != "" {
		delete(m.matches, r.matchID)
	}
	delete(m.rooms, r.code)
}

// RemovePractice removes only this owner's exact, stopped practice room. This
// protects a later room from rollback or a late previous-engine cleanup.
func (m *Manager) RemovePractice(user, code string) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	r, err := m.owned(user)
	if err != nil {
		return err
	}
	if !r.practice {
		return errors.New("not_practice")
	}
	if r.host != user {
		return errors.New("host_required")
	}
	if r.code != code {
		return errors.New("practice_inactive")
	}
	if r.started {
		return errors.New("room_started")
	}
	m.removeRoom(r)
	return nil
}

func (m *Manager) SetPracticeMode(user, matchID, mode string) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	if !shared.IsPracticeMode(mode) {
		return Snapshot{}, errors.New("invalid_bot_mode")
	}
	r, err := m.owned(user)
	if err != nil {
		return Snapshot{}, err
	}
	if !r.practice {
		return Snapshot{}, errors.New("not_practice")
	}
	if r.host != user {
		return Snapshot{}, errors.New("host_required")
	}
	if !r.started || r.matchID != matchID {
		return Snapshot{}, errors.New("practice_inactive")
	}
	r.botMode, r.lastActive = mode, m.now()
	return snapshot(r), nil
}
