package routing

import (
	"encoding/json"
	"errors"
	"fmt"
	"sync"
	"sync/atomic"

	"github.com/local/codex-companion/relay/internal/protocol"
)

type Role string

const (
	RoleBridge Role = "bridge"
	RoleWeb    Role = "web"
)

var peerSequence atomic.Uint64

type Peer struct {
	ID       string
	Role     Role
	DeviceID string
	Send     chan protocol.Envelope
}

func NewPeer(role Role, deviceID string) *Peer {
	return &Peer{
		ID: fmt.Sprintf("peer-%d", peerSequence.Add(1)), Role: role, DeviceID: deviceID,
		Send: make(chan protocol.Envelope, 64),
	}
}

type pendingRequest struct {
	web *Peer
}

type Hub struct {
	mu      sync.RWMutex
	bridges map[string]*Peer
	webs    map[string]map[string]*Peer
	pending map[string]pendingRequest
}

func NewHub() *Hub {
	return &Hub{
		bridges: make(map[string]*Peer),
		webs:    make(map[string]map[string]*Peer),
		pending: make(map[string]pendingRequest),
	}
}

func (h *Hub) Register(peer *Peer) {
	h.mu.Lock()
	defer h.mu.Unlock()
	if peer.Role == RoleBridge {
		h.bridges[peer.DeviceID] = peer
		h.broadcastLocked(peer.DeviceID, protocol.New("device.online", "", peer.DeviceID, nil, map[string]any{"online": true}))
		return
	}
	if h.webs[peer.DeviceID] == nil {
		h.webs[peer.DeviceID] = make(map[string]*Peer)
	}
	h.webs[peer.DeviceID][peer.ID] = peer
	_, online := h.bridges[peer.DeviceID]
	h.deliver(peer, protocol.New(map[bool]string{true: "device.online", false: "device.offline"}[online], "", peer.DeviceID, nil, map[string]any{"online": online}))
}

func (h *Hub) Unregister(peer *Peer) {
	h.mu.Lock()
	defer h.mu.Unlock()
	if peer.Role == RoleBridge {
		if current := h.bridges[peer.DeviceID]; current == peer {
			delete(h.bridges, peer.DeviceID)
			h.broadcastLocked(peer.DeviceID, protocol.New("device.offline", "", peer.DeviceID, nil, map[string]any{"online": false}))
		}
	} else if peers := h.webs[peer.DeviceID]; peers != nil {
		delete(peers, peer.ID)
		if len(peers) == 0 {
			delete(h.webs, peer.DeviceID)
		}
	}
	for requestID, request := range h.pending {
		if request.web == peer {
			delete(h.pending, requestID)
		}
	}
}

func (h *Hub) Handle(peer *Peer, envelope protocol.Envelope) error {
	h.mu.Lock()
	defer h.mu.Unlock()
	envelope.DeviceID = peer.DeviceID
	if peer.Role == RoleWeb {
		return h.handleWebLocked(peer, envelope)
	}
	return h.handleBridgeLocked(peer, envelope)
}

func (h *Hub) IsBridgeOnline(deviceID string) bool {
	h.mu.RLock()
	defer h.mu.RUnlock()
	_, ok := h.bridges[deviceID]
	return ok
}

func (h *Hub) SendToBridge(deviceID string, envelope protocol.Envelope) bool {
	h.mu.RLock()
	defer h.mu.RUnlock()
	peer := h.bridges[deviceID]
	return peer != nil && h.deliver(peer, envelope)
}

func (h *Hub) handleWebLocked(peer *Peer, envelope protocol.Envelope) error {
	if !allowedWebType(envelope.Type) {
		h.deliver(peer, protocol.Error(envelope.RequestID, peer.DeviceID, "UNAUTHORIZED", "该消息类型不允许从手机端发送。"))
		return errors.New("web attempted disallowed message type")
	}
	if envelope.RequestID == "" {
		h.deliver(peer, protocol.Error("", peer.DeviceID, "INVALID_REQUEST", "requestId 不能为空。"))
		return errors.New("missing request id")
	}
	if envelope.Type == "message.send" && len(envelope.Payload) > 17<<20 {
		h.deliver(peer, protocol.Error(envelope.RequestID, peer.DeviceID, "ATTACHMENT_TOO_LARGE", "消息附件超过 Relay 限制。"))
		return errors.New("message payload too large")
	}
	bridge := h.bridges[peer.DeviceID]
	if bridge == nil {
		h.deliver(peer, protocol.Error(envelope.RequestID, peer.DeviceID, "DEVICE_OFFLINE", "电脑 Bridge 当前离线。"))
		return nil
	}
	if _, exists := h.pending[envelope.RequestID]; exists {
		h.deliver(peer, protocol.Error(envelope.RequestID, peer.DeviceID, "DUPLICATE_REQUEST_ID", "requestId 已在处理中。"))
		return errors.New("duplicate request id")
	}
	h.pending[envelope.RequestID] = pendingRequest{web: peer}
	if !h.deliver(bridge, envelope) {
		delete(h.pending, envelope.RequestID)
		h.deliver(peer, protocol.Error(envelope.RequestID, peer.DeviceID, "DEVICE_OFFLINE", "电脑 Bridge 当前不可用。"))
	}
	return nil
}

func (h *Hub) handleBridgeLocked(peer *Peer, envelope protocol.Envelope) error {
	if !allowedBridgeType(envelope.Type) {
		return errors.New("bridge attempted disallowed message type")
	}

	if isBroadcastType(envelope.Type) {
		h.broadcastLocked(peer.DeviceID, envelope)
		return nil
	}
	if envelope.RequestID == "" {
		return errors.New("correlated bridge response missing request id")
	}
	request, ok := h.pending[envelope.RequestID]
	if !ok || request.web.DeviceID != peer.DeviceID {
		return errors.New("unknown request correlation")
	}
	h.deliver(request.web, envelope)
	if isTerminalResponse(envelope.Type) {
		delete(h.pending, envelope.RequestID)
	}
	return nil
}

func (h *Hub) broadcastLocked(deviceID string, envelope protocol.Envelope) {
	for _, peer := range h.webs[deviceID] {
		h.deliver(peer, envelope)
	}
}

func (h *Hub) deliver(peer *Peer, envelope protocol.Envelope) bool {
	select {
	case peer.Send <- envelope:
		return true
	default:
		return false
	}
}

func allowedWebType(value string) bool {
	switch value {
	case "thread.list.request", "thread.create.request", "thread.read.request", "media.read.request", "message.send", "codex.stop":
		return true
	default:
		return false
	}
}

func allowedBridgeType(value string) bool {
	switch value {
	case "thread.list.response", "thread.create.response", "thread.create.failed", "thread.read.response", "media.read.response", "thread.updated",
		"message.accepted", "message.confirmed", "message.failed",
		"codex.stop.response", "codex.stop.failed",
		"bridge.status", "codex.status", "error":
		return true
	default:
		return false
	}
}

func isBroadcastType(value string) bool {
	switch value {
	case "thread.updated", "bridge.status", "codex.status":
		return true
	default:
		return false
	}
}

func isTerminalResponse(value string) bool {
	switch value {
	case "thread.list.response", "thread.create.response", "thread.create.failed", "thread.read.response", "media.read.response", "message.confirmed", "message.failed",
		"codex.stop.response", "codex.stop.failed", "error":
		return true
	default:
		return false
	}
}

func Payload[T any](envelope protocol.Envelope) (T, error) {
	var value T
	if len(envelope.Payload) == 0 {
		return value, nil
	}
	return value, json.Unmarshal(envelope.Payload, &value)
}
