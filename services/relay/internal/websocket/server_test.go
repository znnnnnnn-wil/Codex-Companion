package websocketserver

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"
	"github.com/coder/websocket/wsjson"
	"github.com/local/codex-companion/relay/internal/pairing"
	"github.com/local/codex-companion/relay/internal/protocol"
	"github.com/local/codex-companion/relay/internal/routing"
	"github.com/local/codex-companion/relay/internal/storage"
)

type unavailableStore struct{ storage.Store }

func (s unavailableStore) Authenticate(context.Context, string, string, []byte) (bool, error) {
	return false, errors.New("database unavailable")
}

func TestAuthenticationProbeDoesNotReplaceBridge(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	service := pairing.NewService(storage.NewMemoryStore(), time.Minute)
	created, err := service.Create(ctx, "test")
	if err != nil {
		t.Fatal(err)
	}
	if _, err := service.Claim(ctx, created.Code); err != nil {
		t.Fatal(err)
	}
	hub := routing.NewHub()
	original := routing.NewPeer(routing.RoleBridge, created.DeviceID)
	hub.Register(original)
	web := routing.NewPeer(routing.RoleWeb, created.DeviceID)
	hub.Register(web)
	<-web.Send // initial online notification
	server := New(hub, service, nil, slog.New(slog.NewTextHandler(io.Discard, nil)))
	httpServer := httptest.NewServer(httpHandler(server))
	defer httpServer.Close()
	for _, credential := range []string{created.BridgeCredential, "stale"} {
		response := exchange(t, ctx, httpServer.URL, "device.auth.check", created.DeviceID, credential)
		if response.Type != "device.auth.result" {
			t.Fatalf("unexpected response: %s", response.Type)
		}
		var result struct {
			Authenticated bool
			Paired        bool
			Code          string
		}
		if err := json.Unmarshal(response.Payload, &result); err != nil {
			t.Fatal(err)
		}
		if result.Authenticated != (credential == created.BridgeCredential) {
			t.Fatal("incorrect authentication result")
		}
		if result.Authenticated && (!result.Paired || result.Code != "OK") {
			t.Fatal("paired credential was not accepted")
		}
		if !result.Authenticated && result.Code != "UNAUTHORIZED" {
			t.Fatal("stale credential was not rejected")
		}
		if !hub.SendToBridge(created.DeviceID, protocol.New("test", "", "", nil, nil)) {
			t.Fatal("original bridge lost")
		}
		select {
		case <-original.Send:
		default:
			t.Fatal("probe replaced the original bridge")
		}
		select {
		case <-web.Send:
			t.Fatal("probe emitted a device state change")
		default:
		}
	}
}

func TestForgottenDeviceAndStorageFailureAreDifferent(t *testing.T) {
	for _, test := range []struct {
		name  string
		store storage.Store
		code  string
	}{
		{"relay restarted", storage.NewMemoryStore(), "UNAUTHORIZED"},
		{"storage outage", unavailableStore{storage.NewMemoryStore()}, "AUTH_UNAVAILABLE"},
	} {
		t.Run(test.name, func(t *testing.T) {
			ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
			defer cancel()
			server := New(routing.NewHub(), pairing.NewService(test.store, time.Minute), nil, slog.New(slog.NewTextHandler(io.Discard, nil)))
			httpServer := httptest.NewServer(httpHandler(server))
			defer httpServer.Close()
			for _, message := range []string{"device.auth.check", "device.hello"} {
				response := exchange(t, ctx, httpServer.URL, message, "forgotten-device", "old-credential")
				var result struct{ Code string }
				if err := json.Unmarshal(response.Payload, &result); err != nil {
					t.Fatal(err)
				}
				if result.Code != test.code {
					t.Fatalf("%s: got %s, want %s", message, result.Code, test.code)
				}
			}
		})
	}
}

func TestHelloAcknowledgesAuthenticationAndPendingPairing(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	service := pairing.NewService(storage.NewMemoryStore(), time.Minute)
	created, err := service.Create(ctx, "pending")
	if err != nil {
		t.Fatal(err)
	}
	server := New(routing.NewHub(), service, nil, slog.New(slog.NewTextHandler(io.Discard, nil)))
	httpServer := httptest.NewServer(httpHandler(server))
	defer httpServer.Close()
	response := exchange(t, ctx, httpServer.URL, "device.hello", created.DeviceID, created.BridgeCredential)
	if response.Type != "device.authenticated" {
		t.Fatalf("missing authentication ack: %s", response.Type)
	}
	var result struct {
		Authenticated bool
		Paired        bool
	}
	if err := json.Unmarshal(response.Payload, &result); err != nil {
		t.Fatal(err)
	}
	if !result.Authenticated || result.Paired {
		t.Fatal("unclaimed device must authenticate but still need pairing")
	}
}

func exchange(t *testing.T, ctx context.Context, url, typ, deviceID, credential string) protocol.Envelope {
	t.Helper()
	connection, _, err := websocket.Dial(ctx, "ws"+strings.TrimPrefix(url, "http"), nil)
	if err != nil {
		t.Fatal(err)
	}
	defer connection.CloseNow()
	err = wsjson.Write(ctx, connection, protocol.New(typ, "probe-request", "", nil, map[string]any{
		"deviceId": deviceID, "credential": credential, "acknowledge": true,
	}))
	if err != nil {
		t.Fatal(err)
	}
	var response protocol.Envelope
	if err := wsjson.Read(ctx, connection, &response); err != nil {
		t.Fatal(err)
	}
	if response.RequestID != "probe-request" {
		t.Fatal("response correlation lost")
	}
	if strings.Contains(string(response.Payload), credential) {
		t.Fatal("response leaked credential")
	}
	return response
}

func httpHandler(server *Server) http.Handler { return http.HandlerFunc(server.Bridge) }
