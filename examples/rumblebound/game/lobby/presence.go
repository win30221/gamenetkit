package lobby

import (
	"sort"
	"time"
)

// Lobby polls renew an individual member's lease. A closed tab cannot remain
// room owner indefinitely merely because another member is still polling.
const MemberIdleTimeout = 2 * time.Minute

func (m *Manager) Touch(user string) {
	m.mu.Lock()
	defer m.mu.Unlock()
	if r, err := m.owned(user); err == nil {
		if r.presence == nil {
			r.presence = make(map[string]time.Time)
		}
		r.presence[user] = m.now()
	}
}

// PruneMembers leaves frozen battle rosters intact. Their disconnect/reconnect
// policy belongs to the match; expired identities leave the lobby after End.
func (m *Manager) PruneMembers(userActive func(string) bool) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.prune()
	cutoff := m.now().Add(-MemberIdleTimeout)
	for _, r := range m.rooms {
		if r.started {
			continue
		}
		for user, member := range r.members {
			if member.Bot {
				continue
			}
			seen, tracked := r.presence[user]
			if !userActive(user) || (tracked && !seen.After(cutoff)) {
				m.removeMember(r, user)
			}
		}
	}
}

func (m *Manager) removeMember(r *room, user string) {
	if r.practice {
		m.removeRoom(r)
		return
	}
	delete(r.members, user)
	delete(r.presence, user)
	delete(m.memberRoom, user)
	if len(r.members) == 0 {
		delete(m.rooms, r.code)
		return
	}
	if r.host == user {
		users := make([]string, 0, len(r.members))
		for id := range r.members {
			users = append(users, id)
		}
		sort.Strings(users)
		r.host = users[0]
	}
	r.lastActive = m.now()
}

func (m *Manager) Count() int {
	m.mu.Lock()
	defer m.mu.Unlock()
	return len(m.rooms)
}
