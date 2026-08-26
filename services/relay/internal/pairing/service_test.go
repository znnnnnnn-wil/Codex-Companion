package pairing

import (
	"context"
	"testing"
	"time"

	"github.com/local/codex-companion/relay/internal/storage"
)

func TestPairingCreates256BitCredentialsAndAuthenticatesBothRoles(t *testing.T) {
	ctx := context.Background()
	store := storage.NewMemoryStore()
	service := NewService(store, time.Minute)

	created, err := service.Create(ctx, "PC")
	if err != nil {
		t.Fatal(err)
	}
	if len(created.BridgeCredential) < 43 {
		t.Fatalf("bridge credential too short: %d", len(created.BridgeCredential))
	}
	claimed, err := service.Claim(ctx, created.Code)
	if err != nil {
		t.Fatal(err)
	}
	if claimed.DeviceID != created.DeviceID || len(claimed.WebCredential) < 43 {
		t.Fatal("claim did not return matching device and 256-bit credential")
	}

	bridgeOK, err := service.Authenticate(ctx, created.DeviceID, "bridge", created.BridgeCredential)
	if err != nil || !bridgeOK {
		t.Fatalf("bridge authentication failed: %v", err)
	}
	webOK, err := service.Authenticate(ctx, created.DeviceID, "web", claimed.WebCredential)
	if err != nil || !webOK {
		t.Fatalf("web authentication failed: %v", err)
	}
	wrong, err := service.Authenticate(ctx, created.DeviceID, "web", "wrong")
	if err != nil || wrong {
		t.Fatal("invalid credential was accepted")
	}
}

func TestPairingCodeCanOnlyBeClaimedOnce(t *testing.T) {
	ctx := context.Background()
	service := NewService(storage.NewMemoryStore(), time.Minute)
	created, err := service.Create(ctx, "PC")
	if err != nil {
		t.Fatal(err)
	}
	if _, err = service.Claim(ctx, created.Code); err != nil {
		t.Fatal(err)
	}
	if _, err = service.Claim(ctx, created.Code); err != storage.ErrPairingClaimed {
		t.Fatalf("expected ErrPairingClaimed, got %v", err)
	}
}
