package web

import (
	"embed"
	"net/http"
)

//go:embed google-login.js
var files embed.FS

func GoogleLogin(w http.ResponseWriter, r *http.Request) {
	r2 := r.Clone(r.Context())
	r2.URL.Path = "/google-login.js"
	http.FileServer(http.FS(files)).ServeHTTP(w, r2)
}
