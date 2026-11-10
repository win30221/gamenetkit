package gateway

import "github.com/gin-gonic/gin"

// NewRouter centralizes HTTP routing policy for the lab and its test fixtures.
// In particular, no forwarded address is trusted for application decisions.
func NewRouter() *gin.Engine {
	router := gin.New()
	router.Use(gin.Recovery())
	router.HandleMethodNotAllowed = true
	router.RedirectTrailingSlash = false
	_ = router.SetTrustedProxies(nil)
	return router
}
