package protocol

type NetOptions struct {
	Codec    string  `json:"codec"`
	Quantize bool    `json:"quantize"`
	Interest string  `json:"interest"`
	Radius   float64 `json:"radius"`
	History  bool    `json:"history"`
	Lag      string  `json:"lag"`
}
type TechState struct {
	RewindUsed     bool       `json:"rewindUsed"`
	Options        NetOptions `json:"options"`
	HistorySamples int        `json:"historySamples"`
	RewindTick     uint64     `json:"rewindTick"`
	Visible        int        `json:"visible"`
	Total          int        `json:"total"`
}
type FrameEnvelope struct {
	Type       string `json:"type"`
	Protocol   int    `json:"protocol"`
	Content    string `json:"content"`
	MatchID    string `json:"matchID"`
	Generation uint32 `json:"generation"`
	Tick       uint64 `json:"tick"`
	ID         uint64 `json:"id"`
	Base       uint64 `json:"base"`
	Data       string `json:"data"`
}
