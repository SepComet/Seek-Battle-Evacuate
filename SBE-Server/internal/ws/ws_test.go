package ws

import (
	"net/http"
	"net/http/httptest"
	"testing"
	"time"
)

func TestEchoEndToEnd(t *testing.T) {
	up := &Upgrader{}
	handler := up.Serve("/ws", func(conn *Conn) {
		for {
			_, msg, err := conn.ReadMessage()
			if err != nil {
				return
			}
			if err := conn.WriteMessage(msg); err != nil {
				return
			}
		}
	})
	srv := httptest.NewServer(handler)
	defer srv.Close()

	conn, err := DialClient(srv.Listener.Addr().String(), "/ws")
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	defer func() { _ = conn.Close() }()

	if err := conn.WriteMessage([]byte(`{"t":"ping"}`)); err != nil {
		t.Fatalf("write: %v", err)
	}
	_ = conn.SetReadDeadline(time.Now().Add(3 * time.Second))
	_, msg, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("read: %v", err)
	}
	if string(msg) != `{"t":"ping"}` {
		t.Fatalf("echo mismatch: %s", msg)
	}
}

func TestEchoBinaryPayloadEndToEnd(t *testing.T) {
	up := &Upgrader{}
	handler := up.Serve("/ws", func(conn *Conn) {
		for {
			_, msg, err := conn.ReadMessage()
			if err != nil {
				return
			}
			if err := conn.WriteMessage(msg); err != nil {
				return
			}
		}
	})
	srv := httptest.NewServer(handler)
	defer srv.Close()

	conn, err := DialClient(srv.Listener.Addr().String(), "/ws")
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	defer func() { _ = conn.Close() }()

	// 大消息（超过单帧 125/65535 档位）验证分片与重组
	big := make([]byte, 200000)
	for i := range big {
		big[i] = byte(i % 251)
	}
	if err := conn.WriteMessage(big); err != nil {
		t.Fatalf("write big: %v", err)
	}
	_ = conn.SetReadDeadline(time.Now().Add(3 * time.Second))
	op, got, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("read big: %v", err)
	}
	if op != OpText || len(got) != len(big) {
		t.Fatalf("big payload mismatch: op=%d len=%d", op, len(got))
	}
	for i := range big {
		if got[i] != big[i] {
			t.Fatalf("big payload corrupted at %d", i)
		}
	}
}

func TestRejectsNonWebSocketRequest(t *testing.T) {
	up := &Upgrader{}
	srv := httptest.NewServer(up.Serve("/ws", func(conn *Conn) {}))
	defer srv.Close()

	resp, err := http.Get(srv.URL + "/ws")
	if err != nil {
		t.Fatalf("get: %v", err)
	}
	defer func() { _ = resp.Body.Close() }()
	if resp.StatusCode != http.StatusBadRequest {
		t.Fatalf("want 400 for plain GET, got %d", resp.StatusCode)
	}
}
