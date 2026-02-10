package micshooksdk

type HookMeta struct {
	TenantID    string
	RequestID   string
	TimestampMs int64
	Sign        string
	TraceID     string
}

type TenantRuntimeConfig struct {
	HookBaseURL             string
	HeartbeatTimeoutSeconds int32
	OfflineBufferTTLSeconds int32
	TenantMaxConnections    int32
	UserMaxConnections      int32
	TenantMaxMessageQps     int32
	TenantSecret            string

	// Optional hook isolation overrides (may be zero if unset).
	HookMaxConcurrency         *int32
	HookQueueTimeoutMs         *int32
	HookBreakerFailureThreshold *int32
	HookBreakerOpenMs          *int32
	HookSignRequired           *bool
	OfflineUseHookPull         *bool
}

type AuthRequest struct {
	Meta     HookMeta
	Token    string
	DeviceID string
}

type AuthResponse struct {
	Meta     HookMeta
	Ok       bool
	UserID   string
	DeviceID string
	Config   *TenantRuntimeConfig
	Reason   string
}

// CheckMessageRequest contains Meta and an opaque MessageRequest payload (bytes).
// The message is intentionally not parsed by the SDK to keep it lightweight.
type CheckMessageRequest struct {
	Meta            HookMeta
	MessageWireBytes []byte
}

type CheckMessageResponse struct {
	Meta   HookMeta
	Allow  bool
	Reason string
}

type GetGroupMembersRequest struct {
	Meta    HookMeta
	GroupID string
}

type GetGroupMembersResponse struct {
	Meta    HookMeta
	UserIDs []string
}

type GetOfflineMessagesRequest struct {
	Meta        HookMeta
	UserID      string
	DeviceID    string
	MaxMessages int32
	Cursor      string
}

type GetOfflineMessagesResponse struct {
	Meta             HookMeta
	Ok               bool
	MessagesWireBytes [][]byte
	Reason           string
	NextCursor       string
	HasMore          bool
}

type EventType int32

const (
	EventConnectOnline  EventType = 0
	EventConnectOffline EventType = 1
	EventSingleChatMsg  EventType = 2
	EventGroupChatMsg   EventType = 3
	EventOfflineMessage EventType = 4
)

type MqEvent struct {
	TenantID  string
	EventType EventType
	MsgID     string
	UserID    string
	DeviceID  string
	ToUserID  string
	GroupID   string
	EventData []byte
	Timestamp int64
	NodeID    string
	Sign      string
	TraceID   string
}
