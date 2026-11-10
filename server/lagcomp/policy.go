package lagcomp

// Bounded authorizes a limited historical query. It does not trust client RTT,
// mutate a world, choose hit geometry or rewind damage/immunity.
type Bounded struct{ MaxTicks uint64 }

func (b Bounded) Target(now, requested, observed uint64) (uint64, bool) {
	if b.MaxTicks == 0 || requested > observed || observed > now || requested > now || now-requested > b.MaxTicks {
		return now, false
	}
	return requested, true
}
