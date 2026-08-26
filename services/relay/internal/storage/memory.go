package storage

import (
	"bytes"
	"context"
	"sync"
	"time"
)

type memoryPairing struct {
	deviceID string
	expires  time.Time
	claimed  bool
}

type MemoryStore struct {
	mu          sync.Mutex
	pairings    map[string]*memoryPairing
	credentials map[string]map[string][]byte
}

func NewMemoryStore() *MemoryStore {
	return &MemoryStore{
		pairings:    make(map[string]*memoryPairing),
		credentials: make(map[string]map[string][]byte),
	}
}

func (s *MemoryStore) CreatePairing(_ context.Context, deviceID, _ string, code string, expiresAt time.Time, bridgeTokenHash []byte) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.pairings[code] = &memoryPairing{deviceID: deviceID, expires: expiresAt}
	s.credentials[deviceID] = map[string][]byte{"bridge": bytes.Clone(bridgeTokenHash)}
	return nil
}

func (s *MemoryStore) ClaimPairing(_ context.Context, code string, webTokenHash []byte, now time.Time) (string, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	pairing, ok := s.pairings[code]
	if !ok {
		return "", ErrPairingNotFound
	}
	if pairing.claimed {
		return "", ErrPairingClaimed
	}
	if now.After(pairing.expires) {
		return "", ErrPairingExpired
	}
	pairing.claimed = true
	s.credentials[pairing.deviceID]["web"] = bytes.Clone(webTokenHash)
	return pairing.deviceID, nil
}

func (s *MemoryStore) Authenticate(_ context.Context, deviceID, role string, tokenHash []byte) (bool, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	roles, ok := s.credentials[deviceID]
	if !ok {
		return false, nil
	}
	expected, ok := roles[role]
	return ok && bytes.Equal(expected, tokenHash), nil
}

func (s *MemoryStore) Close() error { return nil }
