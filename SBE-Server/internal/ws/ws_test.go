package ws

import (
	"bufio"
	"bytes"

	"net/http/httptest"
	"testing"
	"time"
)

func readAll(t *testing.T, wire []byte, server bool) (byte, []byte, error) {
	t.Helper()
	conn := NewConn(nil, server)
	conn.br = bufio.NewReader(bytes.NewReader(wire))
	return conn.ReadMessage()
}

func TestFrameRoundTrip(t *testing.T) {
	cases := []struct {
		name    string
		payload []byte
		mask    bool
	}{
		{"small-unmasked", []byte("hello"), false},
		{"small-masked", []byte("hello"), true},
		{"empty", nil, true},
		{"exact-125", bytes.Repeat([]byte{7}, 125), true},
		{"over-125", bytes.Repeat([]byte{1}, 300), true},
		{"over-65535", bytes.Repeat([]byte{2}, 70000), true},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			var buf bytes.Buffer
			key := [4]byte{1, 2, 3, 4}
			if err := writeFrame(&buf, OpText, tc.payload, tc.mask, key); err != nil {
				t.Fatalf("writeFrame: %v", err)
			}
			op, got, err := readAll(t, buf.Bytes(), tc.mask)
			if err != nil {
				t.Fatalf("ReadMessage: %v", err)
			}
			if op != OpText || !bytes.Equal(got, tc.payload) {
				t.Fatalf("round trip mismatch: op=%d len=%d want %d", op, len(got), len(tc.payload))
			}
		})
	}
}

func TestServerRejectsUnmaskedClientFrame(t *testing.T) {
	var buf bytes.Buffer
	if err := writeFrame(&buf, OpText, []byte("x"), false, [4]byte{}); err != nil {
		t.Fatalf("writeFrame: %v", err)
	}
	if _, _, err := readAll(t, buf.Bytes(), true); err == nil {
		t.Fatal("server must reject unmasked client frame")
	}
}

func TestComputeAcceptKey(t *testing.T) {
	// RFC 6455 §1.3 示例
	got := ComputeAcceptKey("dGhlIHNhbXBsZSBub25jZQ==")
	want := "s3pPLMBiTxaQ9kYGzzhZRbK+xOo="
	if got != want {
		t.Fatalf("accept key mismatch: got %s want %s", got, want)
	}
}

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
	_ = conn.conn.SetReadDeadline(time.Now().Add(3 * time.Second))
	_, msg, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("read: %v", err)
	}
	if string(msg) != `{"t":"ping"}` {
		t.Fatalf("echo mismatch: %s", msg)
	}
}
