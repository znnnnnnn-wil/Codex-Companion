package storage

import (
	"context"
	"crypto/sha256"
	"fmt"
	"os"
	"testing"
	"time"
)

func TestPostgresStorePairingAndAuthentication(t *testing.T) {
	databaseURL := os.Getenv("TEST_DATABASE_URL")
	if databaseURL == "" {
		t.Skip("TEST_DATABASE_URL is not set")
	}
	ctx := context.Background()
	store, err := NewPostgresStore(ctx, databaseURL)
	if err != nil {
		t.Fatal(err)
	}
	defer store.Close()

	suffix := time.Now().UnixNano() % 1_000_000_000_000
	deviceID := fmt.Sprintf("00000000-0000-4000-8000-%012d", suffix)
	code := fmt.Sprintf("T%07d", suffix%10_000_000)
	defer store.pool.Exec(ctx, `delete from devices where id = $1`, deviceID)
	bridgeHash := sha256.Sum256([]byte("bridge-secret"))
	webHash := sha256.Sum256([]byte("web-secret"))

	if err := store.CreatePairing(ctx, deviceID, "test", code, time.Now().Add(time.Minute), bridgeHash[:]); err != nil {
		t.Fatal(err)
	}
	if ok, err := store.Authenticate(ctx, deviceID, "bridge", bridgeHash[:]); err != nil || !ok {
		t.Fatalf("bridge auth failed: %v", err)
	}
	claimedDevice, err := store.ClaimPairing(ctx, code, webHash[:], time.Now())
	if err != nil || claimedDevice != deviceID {
		t.Fatalf("claim failed: %v", err)
	}
	if ok, err := store.Authenticate(ctx, deviceID, "web", webHash[:]); err != nil || !ok {
		t.Fatalf("web auth failed: %v", err)
	}
}
