module github.com/win30221/gamenetkit/examples/tower-duel/server

go 1.27.3

require (
	github.com/coder/websocket v1.8.15
	github.com/win30221/gamenetkit/server v0.0.0
)

replace github.com/win30221/gamenetkit/server => ../../../server
