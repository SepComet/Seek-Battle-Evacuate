package ws

import (
	"bufio"
	"crypto/rand"
	"fmt"
	"io"
	"net"
	"sync"
	"time"
)

// Conn 一条 WebSocket 连接的消息级读写。
// server=true 表示服务端方向：要求对端帧必须掩码、本端发送不掩码。
type Conn struct {
	conn   net.Conn
	br     *bufio.Reader
	server bool

	writeMu sync.Mutex

	closeOnce sync.Once
}

// NewConn 包装已建立的（已完成握手的）TCP 连接。
func NewConn(c net.Conn, server bool) *Conn {
	return &Conn{conn: c, br: bufio.NewReader(c), server: server}
}

// ReadMessage 阻塞读取一条完整消息（聚合分片），自动应答 ping、透传 close。
// 返回数据帧的操作码（OpText/OpBinary）与净载荷。
func (c *Conn) ReadMessage() (byte, []byte, error) {
	var msgOp byte
	var msg []byte
	for {
		h, err := readFrameHeader(c.br)
		if err != nil {
			return 0, nil, err
		}

		if h.length > maxMessageSize {
			return 0, nil, fmt.Errorf("ws: frame too large (%d)", h.length)
		}
		if c.server && !h.masked {
			return 0, nil, fmt.Errorf("ws: unmasked client frame")
		}

		payload := make([]byte, h.length)
		if _, err := io.ReadFull(c.br, payload); err != nil {
			return 0, nil, err
		}
		if h.masked {
			applyMask(payload, h.maskKey)
		}

		switch h.opcode {
		case OpPing:
			if err := c.writeControl(OpPong, payload); err != nil {
				return 0, nil, err
			}
			continue
		case OpPong:
			continue
		case OpClose:
			_ = c.writeControl(OpClose, nil)
			return 0, nil, ErrClosed
		case OpText, OpBinary:
			msgOp = h.opcode
			msg = payload
		case opContinuation:
			if msg == nil {
				return 0, nil, fmt.Errorf("ws: unexpected continuation frame")
			}
			msg = append(msg, payload...)
		default:
			return 0, nil, fmt.Errorf("ws: unsupported opcode 0x%X", h.opcode)
		}

		if len(msg) > maxMessageSize {
			return 0, nil, fmt.Errorf("ws: message too large")
		}
		if h.fin {
			return msgOp, msg, nil
		}
	}
}

// WriteMessage 写一条文本消息。
func (c *Conn) WriteMessage(payload []byte) error {
	c.writeMu.Lock()
	defer c.writeMu.Unlock()
	if c.server {
		return writeFrame(c.conn, OpText, payload, false, [4]byte{})
	}
	var key [4]byte
	if _, err := rand.Read(key[:]); err != nil {
		return err
	}
	return writeFrame(c.conn, OpText, payload, true, key)
}

func (c *Conn) writeControl(opcode byte, payload []byte) error {
	c.writeMu.Lock()
	defer c.writeMu.Unlock()
	if c.server {
		return writeFrame(c.conn, opcode, payload, false, [4]byte{})
	}
	var key [4]byte
	if _, err := rand.Read(key[:]); err != nil {
		return err
	}
	return writeFrame(c.conn, opcode, payload, true, key)
}

// Close 发送 close 帧并关闭底层连接（幂等）。
func (c *Conn) Close() error {
	var err error
	c.closeOnce.Do(func() {
		err = c.writeControl(OpClose, nil)
		_ = c.conn.Close()
	})
	return err
}

// RemoteAddr 对端地址。
func (c *Conn) RemoteAddr() net.Addr { return c.conn.RemoteAddr() }

// SetReadDeadline 设置底层连接的读超时。
func (c *Conn) SetReadDeadline(t time.Time) error { return c.conn.SetReadDeadline(t) }
