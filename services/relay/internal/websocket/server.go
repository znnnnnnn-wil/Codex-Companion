package websocketserver

import (
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"net/http"
	"time"

	"github.com/coder/websocket"
	"github.com/coder/websocket/wsjson"
	"github.com/local/codex-companion/relay/internal/pairing"
	"github.com/local/codex-companion/relay/internal/protocol"
	"github.com/local/codex-companion/relay/internal/routing"
)

type Server struct {
	hub            *routing.Hub
	pairing        *pairing.Service
	originPatterns []string
	logger         *slog.Logger
}

const (
	handshakeReadLimit = 64 << 10
	messageReadLimit   = 18 << 20
)

type helloPayload struct {
	DeviceID   string `json:"deviceId"`
	Credential string `json:"credential"`
}

type createPayload struct {
	DeviceName string `json:"deviceName"`
}

type claimPayload struct {
	Code string `json:"code"`
}

func New(hub *routing.Hub, pairingService *pairing.Service, originPatterns []string, logger *slog.Logger) *Server {
	return &Server{hub: hub, pairing: pairingService, originPatterns: originPatterns, logger: logger}
}

func (s *Server) Bridge(w http.ResponseWriter, r *http.Request) {
	s.serve(w, r, routing.RoleBridge)
}

func (s *Server) Web(w http.ResponseWriter, r *http.Request) {
	s.serve(w, r, routing.RoleWeb)
}

func (s *Server) serve(w http.ResponseWriter, r *http.Request, role routing.Role) {
	connection, err := websocket.Accept(w, r, &websocket.AcceptOptions{
		OriginPatterns:  s.originPatterns,
		CompressionMode: websocket.CompressionDisabled,
	})
	if err != nil {
		s.logger.Warn("websocket accept failed", "error", err)
		return
	}
	defer connection.CloseNow()
	connection.SetReadLimit(handshakeReadLimit)

	handshakeContext, cancelHandshake := context.WithTimeout(r.Context(), 10*time.Second)
	var first protocol.Envelope
	err = wsjson.Read(handshakeContext, connection, &first)
	cancelHandshake()
	if err != nil {
		_ = connection.Close(websocket.StatusPolicyViolation, "handshake required")
		return
	}

	peer, err := s.handshake(r.Context(), connection, role, first)
	if err != nil {
		s.logger.Warn("websocket authentication failed", "role", role, "error", err)
		_ = wsjson.Write(r.Context(), connection, protocol.Error(first.RequestID, "", "UNAUTHORIZED", "设备认证或配对失败。"))
		_ = connection.Close(websocket.StatusPolicyViolation, "unauthorized")
		return
	}
	connection.SetReadLimit(messageReadLimit)

	s.hub.Register(peer)
	defer s.hub.Unregister(peer)

	connectionContext, cancel := context.WithCancel(r.Context())
	defer cancel()
	writerDone := make(chan error, 1)
	go func() {
		for {
			select {
			case <-connectionContext.Done():
				writerDone <- connectionContext.Err()
				return
			case envelope := <-peer.Send:
				if err := wsjson.Write(connectionContext, connection, envelope); err != nil {
					writerDone <- err
					return
				}
			}
		}
	}()

	for {
		var envelope protocol.Envelope
		if err := wsjson.Read(connectionContext, connection, &envelope); err != nil {
			break
		}
		if err := s.hub.Handle(peer, envelope); err != nil {
			s.logger.Debug("rejected websocket envelope", "role", role, "type", envelope.Type, "error", err)
		}
		select {
		case <-writerDone:
			return
		default:
		}
	}
	_ = connection.Close(websocket.StatusNormalClosure, "disconnect")
}

func (s *Server) handshake(ctx context.Context, connection *websocket.Conn, role routing.Role, first protocol.Envelope) (*routing.Peer, error) {
	switch first.Type {
	case "device.hello":
		var payload helloPayload
		if err := json.Unmarshal(first.Payload, &payload); err != nil {
			return nil, err
		}
		valid, err := s.pairing.Authenticate(ctx, payload.DeviceID, string(role), payload.Credential)
		if err != nil || !valid {
			return nil, errors.New("invalid credential")
		}
		return routing.NewPeer(role, payload.DeviceID), nil

	case "pairing.create":
		if role != routing.RoleBridge {
			return nil, errors.New("only bridge may create pairing")
		}
		var payload createPayload
		if err := json.Unmarshal(first.Payload, &payload); err != nil {
			return nil, err
		}
		created, err := s.pairing.Create(ctx, payload.DeviceName)
		if err != nil {
			return nil, err
		}
		if err := wsjson.Write(ctx, connection, protocol.New("pairing.created", first.RequestID, created.DeviceID, nil, created)); err != nil {
			return nil, err
		}
		return routing.NewPeer(role, created.DeviceID), nil

	case "pairing.claim":
		if role != routing.RoleWeb {
			return nil, errors.New("only web may claim pairing")
		}
		var payload claimPayload
		if err := json.Unmarshal(first.Payload, &payload); err != nil {
			return nil, err
		}
		claimed, err := s.pairing.Claim(ctx, payload.Code)
		if err != nil {
			return nil, err
		}
		if err := wsjson.Write(ctx, connection, protocol.New("pairing.claimed", first.RequestID, claimed.DeviceID, nil, claimed)); err != nil {
			return nil, err
		}
		s.hub.SendToBridge(claimed.DeviceID, protocol.New("pairing.completed", first.RequestID, claimed.DeviceID, nil, map[string]any{"paired": true}))
		return routing.NewPeer(role, claimed.DeviceID), nil

	default:
		return nil, errors.New("first message must authenticate or pair")
	}
}
