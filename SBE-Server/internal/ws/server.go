package ws

import (
	"crypto/rand"
	"crypto/sha1"
	"encoding/base64"
	"fmt"
	"net"
	"net/http"
	"strings"
)

const websocketGUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

// ComputeAcceptKey 由 Sec-WebSocket-Key 计算握手应答键（导出供测试）。
func ComputeAcceptKey(key string) string {
	sum := sha1.Sum([]byte(key + websocketGUID))
	return base64.StdEncoding.EncodeToString(sum[:])
}

// Upgrader 把 HTTP 请求升级为 WebSocket 连接。
type Upgrader struct {
	// CheckOrigin 校验 Origin 头；nil 表示不校验（内网部署）。
	CheckOrigin func(r *http.Request) bool
}

// Upgrade 完成 HTTP Upgrade 握手并返回消息级连接。
func (u *Upgrader) Upgrade(w http.ResponseWriter, r *http.Request) (*Conn, error) {
	if !strings.EqualFold(r.Header.Get("Upgrade"), "websocket") ||
		!strings.Contains(strings.ToLower(r.Header.Get("Connection")), "upgrade") {
		http.Error(w, "not a websocket handshake", http.StatusBadRequest)
		return nil, fmt.Errorf("ws: invalid handshake")
	}
	key := r.Header.Get("Sec-WebSocket-Key")
	if key == "" {
		http.Error(w, "missing Sec-WebSocket-Key", http.StatusBadRequest)
		return nil, fmt.Errorf("ws: missing key")
	}
	if u.CheckOrigin != nil && !u.CheckOrigin(r) {
		http.Error(w, "origin rejected", http.StatusForbidden)
		return nil, fmt.Errorf("ws: origin rejected")
	}

	hj, ok := w.(http.Hijacker)
	if !ok {
		http.Error(w, "hijack unsupported", http.StatusInternalServerError)
		return nil, fmt.Errorf("ws: response writer cannot hijack")
	}
	conn, rw, err := hj.Hijack()
	if err != nil {
		return nil, fmt.Errorf("ws: hijack: %w", err)
	}
	// rw 内可能已缓冲了握手后的数据，交给 Conn 的 bufio.Reader
	resp := "HTTP/1.1 101 Switching Protocols\r\n" +
		"Upgrade: websocket\r\n" +
		"Connection: Upgrade\r\n" +
		"Sec-WebSocket-Accept: " + ComputeAcceptKey(key) + "\r\n\r\n"
	if _, err := conn.Write([]byte(resp)); err != nil {
		_ = conn.Close()
		return nil, fmt.Errorf("ws: write handshake: %w", err)
	}
	_ = rw.Writer.Flush()

	wsConn := &Conn{conn: conn, br: rw.Reader, server: true}
	return wsConn, nil
}

// Handler 每条连接的处理回调。
type Handler func(conn *Conn)

// Serve 在 HTTP 路径上挂载 WebSocket 处理：每条连接一个 goroutine 运行 handler，
// handler 返回后关闭连接。
func (u *Upgrader) Serve(pattern string, handler Handler) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc(pattern, func(w http.ResponseWriter, r *http.Request) {
		conn, err := u.Upgrade(w, r)
		if err != nil {
			return
		}
		go func() {
			defer func() {
				_ = conn.Close()
				// 防止单条连接的 panic 拖垮整个服务进程
				if rec := recover(); rec != nil {
					fmt.Printf("ws: handler panic: %v\n", rec)
				}
			}()
			handler(conn)
		}()
	})
	return mux
}

// DialClient 以客户端方向连接 ws:// 地址（供集成测试使用）。
// 返回的连接发送的帧带掩码、读取不要求对端掩码。
func DialClient(addr, path string) (*Conn, error) {
	tcp, err := net.Dial("tcp", addr)
	if err != nil {
		return nil, err
	}

	var keyRaw [16]byte
	if _, err := rand.Read(keyRaw[:]); err != nil {
		_ = tcp.Close()
		return nil, err
	}
	key := base64.StdEncoding.EncodeToString(keyRaw[:])

	req := "GET " + path + " HTTP/1.1\r\n" +
		"Host: " + addr + "\r\n" +
		"Upgrade: websocket\r\n" +
		"Connection: Upgrade\r\n" +
		"Sec-WebSocket-Key: " + key + "\r\n" +
		"Sec-WebSocket-Version: 13\r\n\r\n"
	if _, err := tcp.Write([]byte(req)); err != nil {
		_ = tcp.Close()
		return nil, err
	}

	conn := NewConn(tcp, false)
	// 逐行读取握手响应头
	br := conn.br
	status, err := br.ReadString('\n')
	if err != nil {
		_ = tcp.Close()
		return nil, fmt.Errorf("ws: read handshake status: %w", err)
	}
	if !strings.Contains(status, "101") {
		_ = tcp.Close()
		return nil, fmt.Errorf("ws: handshake status %q", strings.TrimSpace(status))
	}
	for {
		line, err := br.ReadString('\n')
		if err != nil {
			_ = tcp.Close()
			return nil, fmt.Errorf("ws: read handshake header: %w", err)
		}
		if line == "\r\n" || line == "\n" {
			break
		}
		if i := strings.IndexByte(line, ':'); i > 0 {
			name := strings.ToLower(strings.TrimSpace(line[:i]))
			value := strings.TrimSpace(line[i+1:])
			if name == "sec-websocket-accept" && value != ComputeAcceptKey(key) {
				_ = tcp.Close()
				return nil, fmt.Errorf("ws: accept key mismatch")
			}
		}
	}
	return conn, nil
}
