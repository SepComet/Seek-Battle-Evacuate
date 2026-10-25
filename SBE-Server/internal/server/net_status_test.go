package server

import (
	"net/http/httptest"
	"testing"

	"sbe-server/internal/protocol"
)

// 账号身份稳定性：uid 全局唯一且跨认证方式稳定；凭据错误不泄露身份。
func TestRegisterLoginIdentityStability(t *testing.T) {
	srv := New(items(), limits())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()
	addr := ts.Listener.Addr().String()

	first := dial(t, addr).call(protocol.Envelope{T: protocol.MsgRegister, Username: "status-player", Password: "secret-7"})
	if first.T != protocol.MsgOK || first.UserID <= 0 || first.Token == "" || first.Snapshot == nil {
		t.Fatalf("register missing identity or login data: %+v", first)
	}
	auth := dial(t, addr).call(protocol.Envelope{T: protocol.MsgAuth, Token: first.Token})
	if auth.T != protocol.MsgOK || auth.UserID != first.UserID || auth.Snapshot == nil {
		t.Fatalf("reconnect changed identity: first=%d auth=%+v", first.UserID, auth)
	}
	again := dial(t, addr).call(protocol.Envelope{T: protocol.MsgLogin, Username: "status-player", Password: "secret-7"})
	if again.T != protocol.MsgOK || again.UserID != first.UserID || again.Token == "" {
		t.Fatalf("login changed identity: first=%d again=%+v", first.UserID, again)
	}
	wrongPass := dial(t, addr).call(protocol.Envelope{T: protocol.MsgLogin, Username: "status-player", Password: "wrong-pass"})
	if wrongPass.T != protocol.MsgErr || wrongPass.Code != protocol.ErrCodeUnauthorized || wrongPass.UserID != 0 {
		t.Fatalf("wrong password must be unauthorized without identity: %+v", wrongPass)
	}
	duplicate := dial(t, addr).call(protocol.Envelope{T: protocol.MsgRegister, Username: "status-player", Password: "secret-8"})
	if duplicate.T != protocol.MsgErr || duplicate.Code != protocol.ErrCodeConflict {
		t.Fatalf("duplicate username must be conflict: %+v", duplicate)
	}
	other := dial(t, addr).call(protocol.Envelope{T: protocol.MsgRegister, Username: "status-other", Password: "secret-9"})
	if other.T != protocol.MsgOK || other.UserID <= 0 || other.UserID == first.UserID {
		t.Fatalf("different accounts share an identity: first=%d other=%+v", first.UserID, other)
	}
	bad := dial(t, addr).call(protocol.Envelope{T: protocol.MsgAuth, Token: "invalid"})
	if bad.T != protocol.MsgErr || bad.Code != protocol.ErrCodeUnauthorized || bad.UserID != 0 {
		t.Fatalf("invalid credentials received an identity: %+v", bad)
	}
}

func TestPingRequiresLoginAndReturnsOnlyAcknowledgement(t *testing.T) {
	srv := New(items(), limits())
	ts := httptest.NewServer(srv.Handler("/ws"))
	defer ts.Close()
	c := dial(t, ts.Listener.Addr().String())
	unauthorized := c.call(protocol.Envelope{T: protocol.MsgPing, UserID: 123})
	if unauthorized.T != protocol.MsgErr || unauthorized.Code != protocol.ErrCodeUnauthorized {
		t.Fatalf("unauthenticated ping accepted: %+v", unauthorized)
	}
	reg := c.call(protocol.Envelope{T: protocol.MsgRegister, Username: "ping-player", Password: "secret-a"})
	if reg.T != protocol.MsgOK {
		t.Fatalf("register: %+v", reg)
	}
	ping := c.call(protocol.Envelope{T: protocol.MsgPing})
	expected := protocol.Envelope{T: protocol.MsgOK, V: protocol.Version, RID: c.rid}
	if ping.T != expected.T || ping.V != expected.V || ping.RID != expected.RID ||
		ping.Snapshot != nil || ping.History != nil || ping.Token != "" || ping.UserID != 0 {
		t.Fatalf("ping must return only t/v/rid, got %+v", ping)
	}
}

func TestPingDoesNotAccessStorage(t *testing.T) {
	// Deliberately leave Store nil: a probe must work from authenticated session data alone.
	srv := &Server{}
	resp := srv.dispatch(&session{userID: 123}, protocol.Envelope{T: protocol.MsgPing, RID: 42})
	if resp.T != protocol.MsgOK || resp.RID != 42 || resp.Snapshot != nil {
		t.Fatalf("probe needs no storage: %+v", resp)
	}
}
