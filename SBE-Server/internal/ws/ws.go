// Package ws 基于 gorilla/websocket 的传输层薄包装：
// 握手升级、消息读写（自动处理掩码、分片、ping/pong、close 帧）与连接生命周期。
// 保持与旧手写实现一致的公共接口，应用层（internal/server）无需感知实现替换。
package ws

import (
	"net"
	"net/http"
	"sync"
	"time"

	"github.com/gorilla/websocket"
)

// maxMessageSize 单条消息上限（1 MiB），防御异常客户端。
const maxMessageSize = 1 << 20

// OpText 文本消息操作码（透传 gorilla 常量）。
const OpText = websocket.TextMessage

// Conn 一条 WebSocket 连接的消息级读写（线程安全的写、串行的读）。
type Conn struct {
	conn      *websocket.Conn
	writeMu   sync.Mutex
	closeOnce sync.Once
}

// NewConn 包装已完成握手的 gorilla 连接。
func NewConn(c *websocket.Conn) *Conn {
	c.SetReadLimit(maxMessageSize)
	return &Conn{conn: c}
}

// ReadMessage 阻塞读取一条完整消息，返回操作码与净载荷。
func (c *Conn) ReadMessage() (byte, []byte, error) {
	t, data, err := c.conn.ReadMessage()
	return byte(t), data, err
}

// WriteMessage 写一条文本消息。
func (c *Conn) WriteMessage(payload []byte) error {
	c.writeMu.Lock()
	defer c.writeMu.Unlock()
	return c.conn.WriteMessage(websocket.TextMessage, payload)
}

// Close 发送 close 帧并关闭底层连接（幂等）。
func (c *Conn) Close() error {
	var err error
	c.closeOnce.Do(func() {
		deadline := time.Now().Add(time.Second)
		_ = c.conn.WriteControl(websocket.CloseMessage,
			websocket.FormatCloseMessage(websocket.CloseNormalClosure, ""), deadline)
		err = c.conn.Close()
	})
	return err
}

// SetReadDeadline 设置底层连接的读超时。
func (c *Conn) SetReadDeadline(t time.Time) error {
	return c.conn.SetReadDeadline(t)
}

// RemoteAddr 对端地址。
func (c *Conn) RemoteAddr() net.Addr { return c.conn.RemoteAddr() }

// Handler 每条连接的处理回调。
type Handler func(conn *Conn)

// Upgrader 把 HTTP 请求升级为 WebSocket 连接。
type Upgrader struct {
	// CheckOrigin 校验 Origin 头；nil 使用 gorilla 默认（无 Origin 头放行，内网部署适用）。
	CheckOrigin func(r *http.Request) bool
}

func (u *Upgrader) gorilla() *websocket.Upgrader {
	return &websocket.Upgrader{
		ReadBufferSize:  4096,
		WriteBufferSize: 4096,
		CheckOrigin:     u.CheckOrigin,
	}
}

// Upgrade 完成 HTTP Upgrade 握手并返回消息级连接。
func (u *Upgrader) Upgrade(w http.ResponseWriter, r *http.Request) (*Conn, error) {
	conn, err := u.gorilla().Upgrade(w, r, nil)
	if err != nil {
		return nil, err
	}
	return NewConn(conn), nil
}

// Serve 在 HTTP 路径上挂载 WebSocket 处理：每条连接一个 goroutine 运行 handler，
// handler 返回后关闭连接；单条连接的 panic 不拖垮服务进程。
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
				_ = recover()
			}()
			handler(conn)
		}()
	})
	return mux
}

// DialClient 以客户端方向连接 ws:// 地址（供集成测试使用）。
func DialClient(addr, path string) (*Conn, error) {
	dialer := websocket.Dialer{HandshakeTimeout: 5 * time.Second}
	c, _, err := dialer.Dial("ws://"+addr+path, nil)
	if err != nil {
		return nil, err
	}
	return NewConn(c), nil
}
