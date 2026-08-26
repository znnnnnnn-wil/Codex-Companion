package routing

import (
	"encoding/json"
	"testing"
	"time"

	"github.com/local/codex-companion/relay/internal/protocol"
)

func TestRoutesRequestsAndCorrelatedResponsesToOriginatingWebPeer(t *testing.T) {
	hub := NewHub()
	bridge := NewPeer(RoleBridge, "device")
	webOne := NewPeer(RoleWeb, "device")
	webTwo := NewPeer(RoleWeb, "device")
	hub.Register(bridge)
	hub.Register(webOne)
	hub.Register(webTwo)
	drain(webOne)
	drain(webTwo)

	request := protocol.New("thread.read.request", "request-1", "spoofed", stringPointer("thread"), map[string]any{})
	if err := hub.Handle(webOne, request); err != nil {
		t.Fatal(err)
	}
	routed := receive(t, bridge)
	if routed.DeviceID != "device" || routed.RequestID != "request-1" {
		t.Fatalf("request was not normalized and routed: %+v", routed)
	}

	response := protocol.New("thread.read.response", "request-1", "device", stringPointer("thread"), map[string]any{"ok": true})
	if err := hub.Handle(bridge, response); err != nil {
		t.Fatal(err)
	}
	if got := receive(t, webOne); got.Type != "thread.read.response" {
		t.Fatalf("unexpected response: %+v", got)
	}
	assertNoMessage(t, webTwo)
	if err := hub.Handle(bridge, response); err == nil {
		t.Fatal("terminal response left stale request correlation")
	}
}

func TestOfflineDeviceReturnsCorrelatedError(t *testing.T) {
	hub := NewHub()
	web := NewPeer(RoleWeb, "device")
	hub.Register(web)
	status := receive(t, web)
	if status.Type != "device.offline" {
		t.Fatalf("expected offline status, got %s", status.Type)
	}

	request := protocol.New("thread.list.request", "request-2", "device", nil, map[string]any{})
	if err := hub.Handle(web, request); err != nil {
		t.Fatal(err)
	}
	errorEnvelope := receive(t, web)
	var payload protocol.ErrorPayload
	if err := json.Unmarshal(errorEnvelope.Payload, &payload); err != nil {
		t.Fatal(err)
	}
	if errorEnvelope.RequestID != "request-2" || payload.Code != "DEVICE_OFFLINE" {
		t.Fatalf("unexpected offline error: %+v %+v", errorEnvelope, payload)
	}
}

func TestBridgeDisconnectBroadcastsOffline(t *testing.T) {
	hub := NewHub()
	bridge := NewPeer(RoleBridge, "device")
	web := NewPeer(RoleWeb, "device")
	hub.Register(bridge)
	hub.Register(web)
	if online := receive(t, web); online.Type != "device.online" {
		t.Fatalf("expected online, got %s", online.Type)
	}

	hub.Unregister(bridge)
	if offline := receive(t, web); offline.Type != "device.offline" {
		t.Fatalf("expected offline, got %s", offline.Type)
	}
}

func TestRejectsDuplicateRequestCorrelation(t *testing.T) {
	hub := NewHub()
	bridge := NewPeer(RoleBridge, "device")
	web := NewPeer(RoleWeb, "device")
	hub.Register(bridge)
	hub.Register(web)
	drain(web)
	request := protocol.New("message.send", "same-id", "device", stringPointer("thread"), map[string]any{"text": "not persisted"})
	if err := hub.Handle(web, request); err != nil {
		t.Fatal(err)
	}
	_ = receive(t, bridge)
	if err := hub.Handle(web, request); err == nil {
		t.Fatal("duplicate request id was accepted")
	}
	if got := receive(t, web); got.Type != "error" {
		t.Fatalf("expected error, got %s", got.Type)
	}
}

func TestRoutesStopAndClearsCorrelationOnResponse(t *testing.T) {
	hub := NewHub()
	bridge := NewPeer(RoleBridge, "device")
	web := NewPeer(RoleWeb, "device")
	hub.Register(bridge)
	hub.Register(web)
	drain(web)

	request := protocol.New("codex.stop", "stop-1", "spoofed", stringPointer("thread"), map[string]any{})
	if err := hub.Handle(web, request); err != nil {
		t.Fatal(err)
	}
	if got := receive(t, bridge); got.Type != "codex.stop" || got.DeviceID != "device" {
		t.Fatalf("unexpected routed stop: %+v", got)
	}
	response := protocol.New("codex.stop.response", "stop-1", "device", stringPointer("thread"), map[string]any{"stopped": true})
	if err := hub.Handle(bridge, response); err != nil {
		t.Fatal(err)
	}
	if got := receive(t, web); got.Type != "codex.stop.response" {
		t.Fatalf("unexpected stop response: %+v", got)
	}
	if err := hub.Handle(bridge, response); err == nil {
		t.Fatal("terminal stop response left stale request correlation")
	}
}

func TestRoutesThreadCreationAndClearsCorrelationOnResponse(t *testing.T) {
	hub := NewHub()
	bridge := NewPeer(RoleBridge, "device")
	web := NewPeer(RoleWeb, "device")
	hub.Register(bridge)
	hub.Register(web)
	drain(web)

	request := protocol.New("thread.create.request", "create-1", "spoofed", nil, map[string]any{"cwd": `C:\repo`})
	if err := hub.Handle(web, request); err != nil {
		t.Fatal(err)
	}
	if got := receive(t, bridge); got.Type != "thread.create.request" || got.DeviceID != "device" {
		t.Fatalf("unexpected routed create request: %+v", got)
	}
	threadID := "new-thread"
	response := protocol.New("thread.create.response", "create-1", "device", &threadID, map[string]any{"thread": map[string]any{"threadId": threadID}})
	if err := hub.Handle(bridge, response); err != nil {
		t.Fatal(err)
	}
	if got := receive(t, web); got.Type != "thread.create.response" || got.ThreadID == nil || *got.ThreadID != threadID {
		t.Fatalf("unexpected create response: %+v", got)
	}
	if err := hub.Handle(bridge, response); err == nil {
		t.Fatal("terminal create response left stale request correlation")
	}
}

func TestRoutesGeneratedMediaWithoutBroadcastingOrPersisting(t *testing.T) {
	hub := NewHub()
	bridge := NewPeer(RoleBridge, "device")
	web := NewPeer(RoleWeb, "device")
	otherWeb := NewPeer(RoleWeb, "device")
	hub.Register(bridge)
	hub.Register(web)
	hub.Register(otherWeb)
	drain(web)
	drain(otherWeb)

	request := protocol.New("media.read.request", "media-1", "spoofed", stringPointer("thread"), map[string]any{"itemId": "image-1"})
	if err := hub.Handle(web, request); err != nil {
		t.Fatal(err)
	}
	if got := receive(t, bridge); got.Type != "media.read.request" || got.DeviceID != "device" {
		t.Fatalf("unexpected media request: %+v", got)
	}
	response := protocol.New("media.read.response", "media-1", "device", stringPointer("thread"), map[string]any{
		"itemId": "image-1", "mimeType": "image/png", "dataBase64": "iVBORw0KGgo",
	})
	if err := hub.Handle(bridge, response); err != nil {
		t.Fatal(err)
	}
	if got := receive(t, web); got.Type != "media.read.response" {
		t.Fatalf("unexpected media response: %+v", got)
	}
	assertNoMessage(t, otherWeb)
	if err := hub.Handle(bridge, response); err == nil {
		t.Fatal("terminal media response left stale request correlation")
	}
}

func TestRejectsOversizedMessagePayloadBeforeRouting(t *testing.T) {
	hub := NewHub()
	bridge := NewPeer(RoleBridge, "device")
	web := NewPeer(RoleWeb, "device")
	hub.Register(bridge)
	hub.Register(web)
	drain(web)

	request := protocol.New("message.send", "large-1", "device", stringPointer("thread"), map[string]any{})
	request.Payload = make([]byte, (17<<20)+1)
	if err := hub.Handle(web, request); err == nil {
		t.Fatal("oversized payload was accepted")
	}
	if got := receive(t, web); got.Type != "error" {
		t.Fatalf("expected error, got %s", got.Type)
	}
	assertNoMessage(t, bridge)
}

func receive(t *testing.T, peer *Peer) protocol.Envelope {
	t.Helper()
	select {
	case message := <-peer.Send:
		return message
	case <-time.After(time.Second):
		t.Fatal("timed out waiting for peer message")
		return protocol.Envelope{}
	}
}

func drain(peer *Peer) {
	for {
		select {
		case <-peer.Send:
		default:
			return
		}
	}
}

func assertNoMessage(t *testing.T, peer *Peer) {
	t.Helper()
	select {
	case message := <-peer.Send:
		t.Fatalf("unexpected message: %+v", message)
	case <-time.After(20 * time.Millisecond):
	}
}

func stringPointer(value string) *string { return &value }
