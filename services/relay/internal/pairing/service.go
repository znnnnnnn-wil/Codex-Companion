package pairing

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"fmt"
	"strings"
	"time"

	"github.com/local/codex-companion/relay/internal/storage"
)

const codeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"

type Created struct {
	DeviceID         string `json:"deviceId"`
	Code             string `json:"code"`
	BridgeCredential string `json:"bridgeCredential"`
	ExpiresAt        int64  `json:"expiresAt"`
}

type Claimed struct {
	DeviceID      string `json:"deviceId"`
	WebCredential string `json:"webCredential"`
}

type Service struct {
	store storage.Store
	ttl   time.Duration
}

func NewService(store storage.Store, ttl time.Duration) *Service {
	return &Service{store: store, ttl: ttl}
}

func (s *Service) Create(ctx context.Context, deviceName string) (Created, error) {
	deviceID, err := randomUUID()
	if err != nil {
		return Created{}, err
	}
	credential, err := randomToken(32)
	if err != nil {
		return Created{}, err
	}
	code, err := randomCode(8)
	if err != nil {
		return Created{}, err
	}
	expiresAt := time.Now().UTC().Add(s.ttl)
	if err := s.store.CreatePairing(ctx, deviceID, deviceName, code, expiresAt, HashCredential(credential)); err != nil {
		return Created{}, err
	}
	return Created{DeviceID: deviceID, Code: code, BridgeCredential: credential, ExpiresAt: expiresAt.UnixMilli()}, nil
}

func (s *Service) Claim(ctx context.Context, code string) (Claimed, error) {
	credential, err := randomToken(32)
	if err != nil {
		return Claimed{}, err
	}
	deviceID, err := s.store.ClaimPairing(ctx, strings.ToUpper(strings.TrimSpace(code)), HashCredential(credential), time.Now().UTC())
	if err != nil {
		return Claimed{}, err
	}
	return Claimed{DeviceID: deviceID, WebCredential: credential}, nil
}

func (s *Service) Authenticate(ctx context.Context, deviceID, role, credential string) (bool, error) {
	if deviceID == "" || credential == "" || (role != "bridge" && role != "web") {
		return false, nil
	}
	return s.store.Authenticate(ctx, deviceID, role, HashCredential(credential))
}

func HashCredential(credential string) []byte {
	hash := sha256.Sum256([]byte(credential))
	return hash[:]
}

func randomToken(bytes int) (string, error) {
	value := make([]byte, bytes)
	if _, err := rand.Read(value); err != nil {
		return "", fmt.Errorf("generate credential: %w", err)
	}
	return base64.RawURLEncoding.EncodeToString(value), nil
}

func randomUUID() (string, error) {
	value := make([]byte, 16)
	if _, err := rand.Read(value); err != nil {
		return "", fmt.Errorf("generate device id: %w", err)
	}
	value[6] = (value[6] & 0x0f) | 0x40
	value[8] = (value[8] & 0x3f) | 0x80
	return fmt.Sprintf("%08x-%04x-%04x-%04x-%012x",
		value[0:4], value[4:6], value[6:8], value[8:10], value[10:16]), nil
}

func randomCode(length int) (string, error) {
	raw := make([]byte, length)
	if _, err := rand.Read(raw); err != nil {
		return "", fmt.Errorf("generate pairing code: %w", err)
	}
	result := make([]byte, length)
	for i, value := range raw {
		result[i] = codeAlphabet[int(value)%len(codeAlphabet)]
	}
	return string(result), nil
}
