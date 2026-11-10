// Package identity validates external identities without retaining profile data.
package identity

import (
	"context"
	"crypto/subtle"
	"encoding/base64"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net/http"
	"strings"
	"time"

	"cloud.google.com/go/auth/credentials/idtoken"
)

var ErrInvalidGoogleToken = errors.New("invalid_google_token")

type Google struct {
	ClientID    string
	validator   *idtoken.Validator
	verifySlots chan struct{}
}

func NewGoogle(clientID string) (*Google, error) {
	if len(clientID) > 256 || !strings.HasSuffix(clientID, ".apps.googleusercontent.com") {
		return nil, errors.New("invalid Google OAuth Web client ID")
	}
	v, err := idtoken.NewValidator(&idtoken.ValidatorOptions{
		Client: &http.Client{Timeout: 5 * time.Second},
		Logger: slog.New(slog.NewTextHandler(io.Discard, nil)),
	})
	if err != nil {
		return nil, err
	}
	return &Google{ClientID: clientID, validator: v, verifySlots: make(chan struct{}, 8)}, nil
}

// Subject returns only the verified Google sub. Email/name/avatar are discarded.
func (g *Google) Subject(ctx context.Context, raw, nonce string) (string, error) {
	if len(raw) > 8192 || len(nonce) < 32 {
		return "", ErrInvalidGoogleToken
	}
	// GIS uses RS256. Reject other algorithms before SDK dispatch to IAP keys.
	parts := strings.Split(raw, ".")
	if len(parts) != 3 {
		return "", ErrInvalidGoogleToken
	}
	b, err := base64.RawURLEncoding.DecodeString(parts[0])
	if err != nil {
		return "", ErrInvalidGoogleToken
	}
	var header struct {
		Alg string `json:"alg"`
	}
	if json.Unmarshal(b, &header) != nil || header.Alg != "RS256" {
		return "", ErrInvalidGoogleToken
	}
	select {
	case g.verifySlots <- struct{}{}:
		defer func() { <-g.verifySlots }()
	default:
		return "", errors.New("google_verifier_busy")
	}
	p, err := g.validator.Validate(ctx, raw, g.ClientID)
	if err != nil {
		return "", ErrInvalidGoogleToken
	}
	if (p.Issuer != "https://accounts.google.com" && p.Issuer != "accounts.google.com") || p.Subject == "" || len(p.Subject) > 256 || p.Expires <= time.Now().Unix() || p.IssuedAt > time.Now().Add(30*time.Second).Unix() {
		return "", ErrInvalidGoogleToken
	}
	n, ok := p.Claims["nonce"].(string)
	if !ok || subtle.ConstantTimeCompare([]byte(n), []byte(nonce)) != 1 {
		return "", ErrInvalidGoogleToken
	}
	return p.Subject, nil
}
