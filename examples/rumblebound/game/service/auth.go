package gateway

import (
	"crypto/rand"
	"crypto/sha256"
	"crypto/subtle"
	"encoding/base64"
	"errors"
	"sync"
	"time"

	"github.com/win30221/gamenetkit/examples/rumblebound/game/sim"
)

type session struct {
	UserID  string
	Expires time.Time
}
type ticket struct {
	SessionHash [32]byte
	Expires     time.Time
	Takeover    bool
	MatchID     string
	Purpose     string
}

const (
	ticketPurposeLab  = "lab"
	ticketPurposeRoom = "room"
)

type loginAttempt struct {
	Nonce   string
	Expires time.Time
}

// Auth is deliberately ephemeral G0 identity/session storage, not a game account DB.
type Auth struct {
	mu            sync.Mutex
	keyHash       [32]byte
	sessions      map[[32]byte]session
	tickets       map[[32]byte]ticket
	googleUsers   map[[32]byte]string
	loginAttempts map[[32]byte]loginAttempt
	now           func() time.Time
}

func NewAuth(key string) (*Auth, error) {
	if len(key) < 24 {
		return nil, errors.New("development key must contain at least 24 characters")
	}
	return &Auth{keyHash: sha256.Sum256([]byte(key)), sessions: map[[32]byte]session{}, tickets: map[[32]byte]ticket{}, googleUsers: map[[32]byte]string{}, loginAttempts: map[[32]byte]loginAttempt{}, now: time.Now}, nil
}
func token() string {
	b := make([]byte, 32)
	if _, err := rand.Read(b); err != nil {
		panic(err)
	}
	return base64.RawURLEncoding.EncodeToString(b)
}
func (a *Auth) NewSession(key string) (string, session, error) {
	h := sha256.Sum256([]byte(key))
	if subtle.ConstantTimeCompare(h[:], a.keyHash[:]) != 1 {
		return "", session{}, errors.New("invalid_development_key")
	}
	return a.newEphemeralSession("test_")
}

// GuestSession creates a new temporary identity; it never selects an existing user.
func (a *Auth) GuestSession() (string, session, error) {
	return a.newEphemeralSession("guest_")
}

func (a *Auth) newEphemeralSession(prefix string) (string, session, error) {
	a.mu.Lock()
	defer a.mu.Unlock()
	a.prune()
	if len(a.sessions) >= 256 {
		return "", session{}, errors.New("session_capacity")
	}
	raw := token()
	s := session{UserID: prefix + token(), Expires: a.now().Add(time.Hour)}
	a.sessions[sha256.Sum256([]byte(raw))] = s
	return raw, s, nil
}
func (a *Auth) prune() {
	now := a.now()
	for k, s := range a.sessions {
		if !now.Before(s.Expires) {
			delete(a.sessions, k)
		}
	}
	for k, t := range a.tickets {
		if !now.Before(t.Expires) {
			delete(a.tickets, k)
		}
	}
	for k, attempt := range a.loginAttempts {
		if !now.Before(attempt.Expires) {
			delete(a.loginAttempts, k)
		}
	}
}

// G0 identity mappings are process-local. Persistent UserIDs are a G1 gate.
func (a *Auth) GoogleSession(verifiedSubject string) (string, session, error) {
	a.mu.Lock()
	defer a.mu.Unlock()
	a.prune()
	if verifiedSubject == "" {
		return "", session{}, errors.New("invalid_subject")
	}
	if len(a.sessions) >= 256 {
		return "", session{}, errors.New("session_capacity")
	}
	subHash := sha256.Sum256([]byte("google\x00" + verifiedSubject))
	userID, exists := a.googleUsers[subHash]
	if !exists {
		if len(a.googleUsers) >= 256 {
			return "", session{}, errors.New("identity_capacity")
		}
		userID = "g0_" + token()
		a.googleUsers[subHash] = userID
	}
	raw := token()
	s := session{UserID: userID, Expires: a.now().Add(time.Hour)}
	a.sessions[sha256.Sum256([]byte(raw))] = s
	return raw, s, nil
}
func (a *Auth) BeginGoogle() (string, string, error) {
	a.mu.Lock()
	defer a.mu.Unlock()
	a.prune()
	if len(a.loginAttempts) >= 128 {
		return "", "", errors.New("login_attempt_capacity")
	}
	raw, nonce := token(), token()
	a.loginAttempts[sha256.Sum256([]byte(raw))] = loginAttempt{Nonce: nonce, Expires: a.now().Add(2 * time.Minute)}
	return raw, nonce, nil
}
func (a *Auth) ConsumeGoogle(raw string) (string, error) {
	a.mu.Lock()
	defer a.mu.Unlock()
	h := sha256.Sum256([]byte(raw))
	attempt, ok := a.loginAttempts[h]
	delete(a.loginAttempts, h)
	if !ok || !a.now().Before(attempt.Expires) {
		return "", errors.New("login_attempt_expired")
	}
	return attempt.Nonce, nil
}
func (a *Auth) Valid(hash [32]byte) (session, bool) {
	a.mu.Lock()
	defer a.mu.Unlock()
	s, ok := a.sessions[hash]
	return s, ok && a.now().Before(s.Expires)
}

// UserActive counts identities rather than sessions: logging out one device
// must not evict a room member who is still signed in on another device.
func (a *Auth) UserActive(user string) bool {
	a.mu.Lock()
	defer a.mu.Unlock()
	for _, s := range a.sessions {
		if s.UserID == user && a.now().Before(s.Expires) {
			return true
		}
	}
	return false
}
func (a *Auth) Ticket(raw string, takeover bool) (string, error) {
	return a.issueTicket(raw, sim.MatchID, ticketPurposeLab, takeover)
}

func (a *Auth) TicketForMatch(raw, matchID string, takeover bool) (string, error) {
	if matchID == "" || matchID == sim.MatchID {
		return "", errors.New("invalid_match")
	}
	return a.issueTicket(raw, matchID, ticketPurposeRoom, takeover)
}

func (a *Auth) issueTicket(raw, matchID, purpose string, takeover bool) (string, error) {
	a.mu.Lock()
	defer a.mu.Unlock()
	a.prune()
	h := sha256.Sum256([]byte(raw))
	if _, ok := a.sessions[h]; !ok {
		return "", errors.New("session_expired")
	}
	if len(a.tickets) >= 512 {
		return "", errors.New("ticket_capacity")
	}
	t := token()
	a.tickets[sha256.Sum256([]byte(t))] = ticket{SessionHash: h, Expires: a.now().Add(15 * time.Second), Takeover: takeover, MatchID: matchID, Purpose: purpose}
	return t, nil
}
func (a *Auth) Consume(raw string) (session, ticket, error) {
	a.mu.Lock()
	defer a.mu.Unlock()
	h := sha256.Sum256([]byte(raw))
	t, ok := a.tickets[h]
	delete(a.tickets, h)
	s, valid := a.sessions[t.SessionHash]
	if !ok || !valid || !a.now().Before(t.Expires) || !a.now().Before(s.Expires) {
		return session{}, ticket{}, errors.New("invalid_ticket")
	}
	return s, t, nil
}
func (a *Auth) Revoke(raw string) {
	a.mu.Lock()
	defer a.mu.Unlock()
	delete(a.sessions, sha256.Sum256([]byte(raw)))
}
