package protocol

import (
	"encoding/json"
	"time"
)

type Envelope struct {
	Type      string          `json:"type"`
	RequestID string          `json:"requestId,omitempty"`
	DeviceID  string          `json:"deviceId,omitempty"`
	ThreadID  *string         `json:"threadId,omitempty"`
	Timestamp int64           `json:"timestamp"`
	Payload   json.RawMessage `json:"payload,omitempty"`
}

type ErrorPayload struct {
	Code    string `json:"code"`
	Message string `json:"message"`
}

func New(typ, requestID, deviceID string, threadID *string, payload any) Envelope {
	raw := json.RawMessage(`{}`)
	if payload != nil {
		if encoded, err := json.Marshal(payload); err == nil {
			raw = encoded
		}
	}
	return Envelope{
		Type: typ, RequestID: requestID, DeviceID: deviceID, ThreadID: threadID,
		Timestamp: time.Now().UnixMilli(), Payload: raw,
	}
}

func Error(requestID, deviceID string, code, message string) Envelope {
	return New("error", requestID, deviceID, nil, ErrorPayload{Code: code, Message: message})
}
