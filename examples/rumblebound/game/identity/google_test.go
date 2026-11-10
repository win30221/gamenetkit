package identity

import (
	"context"
	"crypto"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"math/big"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"cloud.google.com/go/auth/credentials/idtoken"
)

func signedToken(t *testing.T, key *rsa.PrivateKey, claims map[string]any) string {
	t.Helper()
	head, _ := json.Marshal(map[string]string{"alg": "RS256", "kid": "local-fixture"})
	body, _ := json.Marshal(claims)
	encoded := base64.RawURLEncoding.EncodeToString(head) + "." + base64.RawURLEncoding.EncodeToString(body)
	hash := sha256.Sum256([]byte(encoded))
	signature, err := rsa.SignPKCS1v15(rand.Reader, key, crypto.SHA256, hash[:])
	if err != nil {
		t.Fatal(err)
	}
	return encoded + "." + base64.RawURLEncoding.EncodeToString(signature)
}
func TestGoogleSignedTokenValidation(t *testing.T) {
	key, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		t.Fatal(err)
	}
	other, err := rsa.GenerateKey(rand.Reader, 2048)
	if err != nil {
		t.Fatal(err)
	}
	jwks := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Cache-Control", "public, max-age=3600")
		json.NewEncoder(w).Encode(map[string]any{"keys": []map[string]string{{"kid": "local-fixture", "kty": "RSA", "alg": "RS256", "n": base64.RawURLEncoding.EncodeToString(key.N.Bytes()), "e": base64.RawURLEncoding.EncodeToString(big.NewInt(int64(key.E)).Bytes())}}})
	}))
	defer jwks.Close()
	g, _ := NewGoogle("fixture.apps.googleusercontent.com")
	g.validator, err = idtoken.NewValidator(&idtoken.ValidatorOptions{Client: jwks.Client(), RS256CertsURL: jwks.URL})
	if err != nil {
		t.Fatal(err)
	}
	nonce := strings.Repeat("n", 43)
	base := func() map[string]any {
		return map[string]any{"iss": "https://accounts.google.com", "aud": g.ClientID, "sub": "verified-subject", "nonce": nonce, "exp": time.Now().Add(time.Hour).Unix(), "iat": time.Now().Unix(), "email": "must-not-be-used@example.test"}
	}
	for _, tc := range []struct {
		name, key string
		value     any
		forged    bool
	}{
		{"valid", "", nil, false}, {"issuer", "iss", "https://evil.test", false}, {"audience", "aud", "attacker.apps.googleusercontent.com", false}, {"expired", "exp", time.Now().Add(-time.Minute).Unix(), false}, {"future-iat", "iat", time.Now().Add(time.Hour).Unix(), false}, {"nonce", "nonce", "wrong", false}, {"empty-subject", "sub", "", false}, {"signature", "", nil, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			claims := base()
			if tc.key != "" {
				claims[tc.key] = tc.value
			}
			signingKey := key
			if tc.forged {
				signingKey = other
			}
			sub, err := g.Subject(context.Background(), signedToken(t, signingKey, claims), nonce)
			if tc.name == "valid" {
				if err != nil || sub != "verified-subject" {
					t.Fatal(sub, err)
				}
			} else if err == nil {
				t.Fatal("invalid token accepted")
			}
		})
	}
	for _, raw := range []string{"malformed", "eyJhbGciOiJub25lIn0.e30.", "eyJhbGciOiJFUzI1NiJ9.e30.AA", strings.Repeat("x", 8193)} {
		if _, err := g.Subject(context.Background(), raw, nonce); err == nil {
			t.Fatal("malformed token accepted")
		}
	}
	// Cached signing keys keep validation independent of per-login key downloads.
	jwks.Close()
	if _, err := g.Subject(context.Background(), signedToken(t, key, base()), nonce); err != nil {
		t.Fatal("cached key unavailable", err)
	}
}
