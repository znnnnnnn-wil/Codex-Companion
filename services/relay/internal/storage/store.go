package storage

import (
	"context"
	"errors"
	"time"
)

var (
	ErrPairingNotFound = errors.New("pairing session not found")
	ErrPairingExpired  = errors.New("pairing session expired")
	ErrPairingClaimed  = errors.New("pairing session already claimed")
)

type Store interface {
	CreatePairing(ctx context.Context, deviceID, deviceName, code string, expiresAt time.Time, bridgeTokenHash []byte) error
	ClaimPairing(ctx context.Context, code string, webTokenHash []byte, now time.Time) (string, error)
	Authenticate(ctx context.Context, deviceID, role string, tokenHash []byte) (bool, error)
	Close() error
}
