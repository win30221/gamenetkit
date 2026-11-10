package gateway

import (
	"context"
	"crypto/subtle"
	"net/http"
	"time"

	"github.com/gin-gonic/gin"
)

func (s *Server) registerGoogle(mux gin.IRouter) {
	allowed := func(w http.ResponseWriter, r *http.Request) bool {
		if r.Header.Get("Origin") == "" || !s.Origin(r) {
			fail(w, 403, "origin_required")
			return false
		}
		if s.Google == nil {
			fail(w, 503, "google_not_configured")
			return false
		}
		return true
	}
	mux.POST("/api/auth/google/start", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		if !allowed(w, r) {
			return
		}
		attempt, nonce, err := s.Auth.BeginGoogle()
		if err != nil {
			fail(w, 429, err.Error())
			return
		}
		http.SetCookie(w, &http.Cookie{Name: "rb_google_attempt", Value: attempt, Path: "/api/auth/google", HttpOnly: true, Secure: r.TLS != nil, SameSite: http.SameSiteStrictMode, MaxAge: 120})
		reply(w, 200, map[string]string{"attemptID": attempt, "nonce": nonce, "clientID": s.Google.ClientID})
	}))
	mux.POST("/api/auth/google/exchange", gin.WrapF(func(w http.ResponseWriter, r *http.Request) {
		if !allowed(w, r) {
			return
		}
		var req struct {
			AttemptID string `json:"attemptID"`
			IDToken   string `json:"idToken"`
		}
		if readJSONLimit(w, r, &req, 12288) != nil {
			fail(w, 400, "invalid_request")
			return
		}
		cookie, err := r.Cookie("rb_google_attempt")
		if err != nil || len(req.AttemptID) != 43 || subtle.ConstantTimeCompare([]byte(cookie.Value), []byte(req.AttemptID)) != 1 {
			fail(w, 403, "csrf_rejected")
			return
		}
		nonce, err := s.Auth.ConsumeGoogle(req.AttemptID)
		http.SetCookie(w, &http.Cookie{Name: "rb_google_attempt", Path: "/api/auth/google", HttpOnly: true, Secure: r.TLS != nil, SameSite: http.SameSiteStrictMode, MaxAge: -1})
		if err != nil {
			fail(w, 401, err.Error())
			return
		}
		ctx, cancel := context.WithTimeout(r.Context(), 5*time.Second)
		defer cancel()
		subject, err := s.Google.Subject(ctx, req.IDToken, nonce)
		if err != nil {
			fail(w, 401, "google_login_failed")
			return
		}
		raw, identity, err := s.Auth.GoogleSession(subject)
		if err != nil {
			fail(w, 429, err.Error())
			return
		}
		http.SetCookie(w, &http.Cookie{Name: "rb_g0", Value: raw, Path: "/", HttpOnly: true, Secure: r.TLS != nil, SameSite: http.SameSiteStrictMode, MaxAge: 3600})
		reply(w, 200, map[string]any{"session": raw, "userID": identity.UserID, "expires": identity.Expires, "provider": "google", "ephemeralAccount": true})
	}))
}
