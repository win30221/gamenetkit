// Package lobby owns private-room membership, preparation, and roster freezes.
// The match service owns the battle itself.
package lobby

import (
	"crypto/rand"
	"encoding/hex"
	"errors"
	"sort"
	"strings"
	"sync"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/shared"
)

const (
	MaxRooms          = 64
	MaxTeams          = 4
	MaxPlayersPerTeam = 4
	MaxPlayersTotal   = 8
	codeLength        = 10
)

type Team struct {
	ID       string `json:"teamID"`
	Capacity int    `json:"capacity"`
}

type Member struct {
	UserID string `json:"userID"`
	TeamID string `json:"teamID"`
	HeroID string `json:"heroID"`
	Ready  bool   `json:"ready"`
	Bot    bool   `json:"bot,omitempty"`
}

type Snapshot struct {
	Code     string   `json:"code"`
	HostID   string   `json:"hostID"`
	Teams    []Team   `json:"teams"`
	Members  []Member `json:"members"`
	CanStart bool     `json:"canStart"`
	Started  bool     `json:"started"`
	Phase    string   `json:"phase,omitempty"`
	MatchID  string   `json:"matchID"`
	Practice bool     `json:"practice,omitempty"`
	BotMode  string   `json:"botMode,omitempty"`
}

type room struct {
	code       string
	host       string
	teams      []Team
	members    map[string]*Member
	lastActive time.Time
	started    bool
	phase      string
	matchID    string
	practice   bool
	botMode    string
	presence   map[string]time.Time
}

// Manager uses authenticated game UserIDs. Room codes find a room; they do not
// grant control of a player's seat or authorize battle traffic.
type Manager struct {
	mu           sync.Mutex
	rooms        map[string]*room
	matches      map[string]string // MatchID -> room code
	memberRoom   map[string]string
	joinAttempts map[string][]time.Time
	now          func() time.Time
}

func New() *Manager {
	return &Manager{
		rooms: map[string]*room{}, matches: map[string]string{}, memberRoom: map[string]string{},
		joinAttempts: map[string][]time.Time{}, now: time.Now,
	}
}

func randomCode() string {
	const alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"
	var b [codeLength]byte
	if _, err := rand.Read(b[:]); err != nil {
		panic(err)
	}
	for i := range b {
		b[i] = alphabet[int(b[i])%len(alphabet)]
	}
	return string(b[:])
}

func randomMatchID() string {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		panic(err)
	}
	return "m_" + hex.EncodeToString(b[:])
}

func (m *Manager) prune() {
	cutoff := m.now().Add(-time.Hour)
	for code, r := range m.rooms {
		if !r.started && r.lastActive.Before(cutoff) {
			for user := range r.members {
				delete(m.memberRoom, user)
			}
			delete(m.rooms, code)
		}
	}
	for user, attempts := range m.joinAttempts {
		fresh := attempts[:0]
		for _, t := range attempts {
			if t.After(m.now().Add(-time.Minute)) {
				fresh = append(fresh, t)
			}
		}
		if len(fresh) == 0 {
			delete(m.joinAttempts, user)
		} else {
			m.joinAttempts[user] = fresh
		}
	}
}

func (m *Manager) owned(user string) (*room, error) {
	code, ok := m.memberRoom[user]
	if !ok {
		return nil, errors.New("not_in_room")
	}
	r := m.rooms[code]
	if r == nil {
		delete(m.memberRoom, user)
		return nil, errors.New("not_in_room")
	}
	return r, nil
}

func (m *Manager) Create(user string, teamCount int) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	if user == "" {
		return Snapshot{}, errors.New("invalid_user")
	}
	if _, exists := m.memberRoom[user]; exists {
		return Snapshot{}, errors.New("already_in_room")
	}
	if teamCount < 2 || teamCount > MaxTeams {
		return Snapshot{}, errors.New("invalid_team_count")
	}
	if len(m.rooms) >= MaxRooms {
		return Snapshot{}, errors.New("room_capacity")
	}
	code := randomCode()
	for m.rooms[code] != nil {
		code = randomCode()
	}
	r := &room{code: code, host: user, members: map[string]*Member{user: {UserID: user, HeroID: "ranger"}}, lastActive: m.now(), presence: map[string]time.Time{user: m.now()}}
	for i := 0; i < teamCount; i++ {
		r.teams = append(r.teams, Team{ID: "t" + string(rune('1'+i)), Capacity: MaxPlayersPerTeam})
	}
	m.rooms[code], m.memberRoom[user] = r, code
	return snapshot(r), nil
}

func (m *Manager) Join(user, code string) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	if user == "" {
		return Snapshot{}, errors.New("invalid_user")
	}
	code = strings.ToUpper(strings.TrimSpace(code))
	if current, exists := m.memberRoom[user]; exists {
		if current == code {
			r := m.rooms[current]
			if r.started {
				return Snapshot{}, errors.New("room_started")
			}
			return snapshot(r), nil
		}
		return Snapshot{}, errors.New("already_in_room")
	}
	if len(m.joinAttempts[user]) >= 10 {
		return Snapshot{}, errors.New("join_rate_limited")
	}
	m.joinAttempts[user] = append(m.joinAttempts[user], m.now())
	if len(code) != codeLength || m.rooms[code] == nil {
		return Snapshot{}, errors.New("room_not_found")
	}
	r := m.rooms[code]
	if r.practice {
		return Snapshot{}, errors.New("practice_private")
	}
	if r.started {
		return Snapshot{}, errors.New("room_started")
	}
	if len(r.members) >= MaxPlayersTotal {
		return Snapshot{}, errors.New("room_full")
	}
	r.members[user] = &Member{UserID: user, HeroID: "ranger"}
	r.presence[user] = m.now()
	m.memberRoom[user] = code
	r.lastActive = m.now()
	return snapshot(r), nil
}

func (m *Manager) Current(user string) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	r, err := m.owned(user)
	if err != nil {
		return Snapshot{}, err
	}
	r.lastActive = m.now()
	return snapshot(r), nil
}

func (m *Manager) SetHero(user, heroID string) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	r, err := m.owned(user)
	if err != nil {
		return Snapshot{}, err
	}
	if r.started {
		return Snapshot{}, errors.New("room_started")
	}
	if _, valid := shared.Rules().Hero(heroID); !valid {
		return Snapshot{}, errors.New("invalid_hero")
	}
	member := r.members[user]
	if member.HeroID != heroID {
		member.HeroID = heroID
		member.Ready = false
	}
	r.lastActive = m.now()
	return snapshot(r), nil
}

func (m *Manager) chooseTeam(r *room, target, teamID string) (Snapshot, error) {
	if r.started {
		return Snapshot{}, errors.New("room_started")
	}
	member := r.members[target]
	if member == nil {
		return Snapshot{}, errors.New("member_not_found")
	}
	if member.Bot {
		return Snapshot{}, errors.New("bot_reserved")
	}
	if r.practice && teamID != member.TeamID {
		return Snapshot{}, errors.New("practice_fixed_teams")
	}
	if teamID != "" {
		capacity := 0
		for _, team := range r.teams {
			if team.ID == teamID {
				capacity = team.Capacity
				break
			}
		}
		if capacity == 0 {
			return Snapshot{}, errors.New("invalid_team")
		}
		if member.TeamID != teamID {
			count := 0
			for _, other := range r.members {
				if other.TeamID == teamID {
					count++
				}
			}
			if count >= capacity {
				return Snapshot{}, errors.New("team_full")
			}
		}
	}
	if member.TeamID != teamID {
		member.TeamID, member.Ready = teamID, false
	}
	r.lastActive = m.now()
	return snapshot(r), nil
}

func (m *Manager) ChooseTeam(user, teamID string) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	r, err := m.owned(user)
	if err != nil {
		return Snapshot{}, err
	}
	return m.chooseTeam(r, user, teamID)
}

// AssignTeam lets the host move a member; only that member's ready state is reset.
func (m *Manager) AssignTeam(host, target, teamID string) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	r, err := m.owned(host)
	if err != nil {
		return Snapshot{}, err
	}
	if r.started {
		return Snapshot{}, errors.New("room_started")
	}
	if r.host != host {
		return Snapshot{}, errors.New("host_required")
	}
	return m.chooseTeam(r, target, teamID)
}

func (m *Manager) SetCapacity(user, teamID string, capacity int) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	r, err := m.owned(user)
	if err != nil {
		return Snapshot{}, err
	}
	if r.started {
		return Snapshot{}, errors.New("room_started")
	}
	if r.host != user {
		return Snapshot{}, errors.New("host_required")
	}
	if r.practice {
		return Snapshot{}, errors.New("practice_fixed_teams")
	}
	if capacity < 1 || capacity > MaxPlayersPerTeam {
		return Snapshot{}, errors.New("invalid_capacity")
	}
	index := -1
	for i, team := range r.teams {
		if team.ID == teamID {
			index = i
			break
		}
	}
	if index < 0 {
		return Snapshot{}, errors.New("invalid_team")
	}
	count := 0
	for _, member := range r.members {
		if member.TeamID == teamID {
			count++
		}
	}
	if count > capacity {
		return Snapshot{}, errors.New("team_over_capacity")
	}
	if r.teams[index].Capacity != capacity {
		r.teams[index].Capacity = capacity
		for _, member := range r.members {
			if member.TeamID == teamID {
				member.Ready = false
			}
		}
	}
	r.lastActive = m.now()
	return snapshot(r), nil
}

func (m *Manager) SetReady(user string, ready bool, protocol int, content string) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	r, err := m.owned(user)
	if err != nil {
		return Snapshot{}, err
	}
	if r.started {
		return Snapshot{}, errors.New("room_started")
	}
	member := r.members[user]
	if ready {
		if member.TeamID == "" {
			return Snapshot{}, errors.New("team_required")
		}
		rules := shared.Rules()
		if protocol != rules.Protocol || content != rules.Content {
			return Snapshot{}, errors.New("version_mismatch")
		}
	}
	member.Ready = ready
	r.lastActive = m.now()
	return snapshot(r), nil
}

func (m *Manager) Leave(user string) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	r, err := m.owned(user)
	if err != nil {
		return err
	}
	if r.started {
		return errors.New("room_started")
	}
	if r.practice {
		m.removeRoom(r)
		return nil
	}
	m.removeMember(r, user)
	return nil
}

// Start freezes the room roster under the manager lock. The returned snapshot
// is detached from the room's slices and maps for match-engine allocation.
func (m *Manager) Start(host string) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	r, err := m.owned(host)
	if err != nil {
		return Snapshot{}, err
	}
	if r.started {
		return Snapshot{}, errors.New("room_started")
	}
	if r.host != host {
		return Snapshot{}, errors.New("host_required")
	}
	if !canStart(r) {
		return Snapshot{}, errors.New("room_not_ready")
	}
	matchID := randomMatchID()
	for m.matches[matchID] != "" {
		matchID = randomMatchID()
	}
	r.matchID, r.started, r.phase, r.lastActive = matchID, true, "loading", m.now()
	m.matches[matchID] = r.code
	return snapshot(r), nil
}

// Begin marks the frozen roster as playing after the match engine has accepted
// a loaded acknowledgement from every member. It never changes membership.
func (m *Manager) Begin(matchID string) bool {
	m.mu.Lock()
	defer m.mu.Unlock()
	r := m.rooms[m.matches[matchID]]
	if r == nil || !r.started || r.matchID != matchID {
		return false
	}
	if r.phase == "playing" {
		return true
	}
	if r.phase != "loading" {
		return false
	}
	r.phase = "playing"
	r.lastActive = m.now()
	return true
}

// Match resolves the frozen match only for one of its authenticated members.
func (m *Manager) Match(user string) (Snapshot, error) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	r, err := m.owned(user)
	if err != nil {
		return Snapshot{}, err
	}
	if !r.started {
		return Snapshot{}, errors.New("match_not_started")
	}
	return snapshot(r), nil
}

// End returns the exact match's room to preparation. Late cleanup for an older
// MatchID cannot reset a newer match in the same room.
func (m *Manager) End(matchID string) {
	m.mu.Lock()
	defer m.mu.Unlock()
	code := m.matches[matchID]
	delete(m.matches, matchID)
	r := m.rooms[code]
	if r == nil || !r.started || r.matchID != matchID {
		return
	}
	r.started, r.phase, r.matchID, r.lastActive = false, "", "", m.now()
	if r.presence == nil {
		r.presence = make(map[string]time.Time)
	}
	for _, member := range r.members {
		member.Ready = member.Bot
		r.presence[member.UserID] = m.now()
	}
}

func snapshot(r *room) Snapshot {
	result := Snapshot{Code: r.code, HostID: r.host, Teams: append([]Team(nil), r.teams...), Started: r.started, Phase: r.phase, MatchID: r.matchID, Practice: r.practice, BotMode: r.botMode}
	for _, member := range r.members {
		result.Members = append(result.Members, *member)
	}
	sort.Slice(result.Members, func(i, j int) bool { return result.Members[i].UserID < result.Members[j].UserID })
	result.CanStart = !r.started && canStart(r)
	return result
}

func canStart(r *room) bool {
	if len(r.members) < 2 {
		return false
	}
	nonempty := map[string]bool{}
	for _, member := range r.members {
		if !member.Ready || member.TeamID == "" {
			return false
		}
		nonempty[member.TeamID] = true
	}
	return len(nonempty) >= 2
}
