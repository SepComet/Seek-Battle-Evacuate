// smoke 对指定 sbe-server 做最小冒烟验证：注册 → ping → 登录。
// 部署后运行：go run ./cmd/smoke -addr 127.0.0.1:7777
package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"time"

	"sbe-server/internal/protocol"
	"sbe-server/internal/ws"
)

func main() {
	if err := run(); err != nil {
		fmt.Fprintf(os.Stderr, "smoke: %v\n", err)
		os.Exit(1)
	}
}

func run() error {
	addr := flag.String("addr", "127.0.0.1:7777", "服务器地址 host:port")
	path := flag.String("path", "/ws", "WebSocket 路径")
	flag.Parse()

	conn, err := ws.DialClient(*addr, *path)
	if err != nil {
		return fmt.Errorf("dial %s%s: %w", *addr, *path, err)
	}
	defer func() { _ = conn.Close() }()

	rid := int64(0)
	call := func(env protocol.Envelope) (protocol.Envelope, error) {
		rid++
		env.RID = rid
		env.V = protocol.Version
		raw, err := json.Marshal(env)
		if err != nil {
			return protocol.Envelope{}, err
		}
		if err := conn.WriteMessage(raw); err != nil {
			return protocol.Envelope{}, err
		}
		_ = conn.SetReadDeadline(time.Now().Add(5 * time.Second))
		_, respRaw, err := conn.ReadMessage()
		if err != nil {
			return protocol.Envelope{}, err
		}
		var resp protocol.Envelope
		if err := json.Unmarshal(respRaw, &resp); err != nil {
			return protocol.Envelope{}, err
		}
		if resp.RID != env.RID {
			return protocol.Envelope{}, fmt.Errorf("rid mismatch: sent %d got %d", env.RID, resp.RID)
		}
		return resp, nil
	}

	// 1. 注册：新账号拿到 token、uid 与快照
	username := fmt.Sprintf("smoke-%d", time.Now().UnixNano())
	reg, err := call(protocol.Envelope{T: protocol.MsgRegister, Username: username, Password: "smoke-pass-1"})
	if err != nil {
		return fmt.Errorf("register: %w", err)
	}
	if reg.T != protocol.MsgOK || reg.Token == "" || reg.UserID <= 0 || reg.Snapshot == nil {
		return fmt.Errorf("register rejected: %+v", reg)
	}
	// 协议 v5：注册快照必须携带按 initialCredits 发放的余额
	if reg.Snapshot.Credits <= 0 {
		return fmt.Errorf("register snapshot credits = %d, want > 0 (initialCredits)", reg.Snapshot.Credits)
	}
	fmt.Printf("register ok: uid=%d username=%s credits=%d\n", reg.UserID, username, reg.Snapshot.Credits)

	// 2. ping：已登录会话往返
	ping, err := call(protocol.Envelope{T: protocol.MsgPing})
	if err != nil {
		return fmt.Errorf("ping: %w", err)
	}
	if ping.T != protocol.MsgOK {
		return fmt.Errorf("ping rejected: %+v", ping)
	}
	fmt.Println("ping ok")

	// 3. 登录：同账密再次认证，uid 一致
	login, err := call(protocol.Envelope{T: protocol.MsgLogin, Username: username, Password: "smoke-pass-1"})
	if err != nil {
		return fmt.Errorf("login: %w", err)
	}
	if login.T != protocol.MsgOK || login.UserID != reg.UserID {
		return fmt.Errorf("login rejected: %+v", login)
	}
	fmt.Printf("login ok: uid=%d\n", login.UserID)

	fmt.Println("== 冒烟通过：register / ping / login ==")
	return nil
}
