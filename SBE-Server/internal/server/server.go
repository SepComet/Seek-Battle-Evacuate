// Package server 组装 ws 传输、会话与存储：把协议信封分派到存储操作并回包。
package server

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"net/http"
	"time"

	"sbe-server/internal/core"
	"sbe-server/internal/memorystore"
	"sbe-server/internal/protocol"
	"sbe-server/internal/store"
	"sbe-server/internal/ws"
)

// ridCacheSize 每连接保留的最近请求应答数，用于消息重试去重。
const ridCacheSize = 64

// Server 应用服务器。
type Server struct {
	Store store.Store
	Items core.ItemTable
}

// New 创建应用服务器（内存存储；生产部署见 cmd/sbe-server 的 mysql 变体）。
func New(items core.ItemTable) *Server {
	return &Server{Store: memorystore.New(), Items: items}
}

// Handler 返回挂载在指定路径的 WebSocket http.Handler。
func (s *Server) Handler(pattern string) http.Handler {
	up := &ws.Upgrader{}
	return up.Serve(pattern, s.handleConn)
}

// session 单条连接的会话状态。
type session struct {
	userID int64
	rids   map[int64]protocol.Envelope
	order  []int64
}

func (sess *session) remember(rid int64, resp protocol.Envelope) {
	if rid == 0 {
		return
	}
	if _, ok := sess.rids[rid]; ok {
		return
	}
	sess.rids[rid] = resp
	sess.order = append(sess.order, rid)
	if len(sess.order) > ridCacheSize {
		delete(sess.rids, sess.order[0])
		sess.order = sess.order[1:]
	}
}

func (s *Server) handleConn(conn *ws.Conn) {
	sess := &session{rids: make(map[int64]protocol.Envelope)}
	defer func() { _ = conn.Close() }()
	log.Printf("[ws] connected %s", conn.RemoteAddr())
	defer log.Printf("[ws] disconnected %s", conn.RemoteAddr())

	for {
		_, raw, err := conn.ReadMessage()
		if err != nil {
			return
		}

		var env protocol.Envelope
		if err := json.Unmarshal(raw, &env); err != nil {
			log.Printf("[ws] %s malformed json (%d bytes): %v", conn.RemoteAddr(), len(raw), err)
			_ = s.writeErr(conn, env, protocol.ErrCodeBadMessage, "malformed json")
			continue
		}
		if env.V != protocol.Version {
			log.Printf("[ws] %s unsupported protocol version %d (t=%s)", conn.RemoteAddr(), env.V, env.T)
			_ = s.writeErr(conn, env, protocol.ErrCodeBadMessage, "unsupported protocol version")
			continue
		}
		if resp, dup := sess.rids[env.RID]; dup {
			log.Printf("[ws] %s %s rid=%d -> cached (retry)", conn.RemoteAddr(), env.T, env.RID)
			_ = s.write(conn, resp)
			continue
		}

		start := time.Now()
		resp := s.dispatch(sess, env)
		sess.remember(env.RID, resp)
		log.Printf("[ws] %s %s rid=%d user=%d ops=%d -> %s%s%s [%s]",
			conn.RemoteAddr(), env.T, env.RID, sess.userID, len(env.Ops),
			resp.T,
			respStatusDetail(&resp),
			detailFor(&env, &resp),
			time.Since(start).Round(time.Microsecond))
		if err := s.write(conn, resp); err != nil {
			return
		}
	}
}

// respStatusDetail 错误响应附上错误码与信息。
func respStatusDetail(resp *protocol.Envelope) string {
	if resp.T == protocol.MsgErr {
		return fmt.Sprintf("(%s: %s)", resp.Code, resp.Msg)
	}
	return ""
}

// detailFor 为关键业务消息补充一行摘要。
func detailFor(env *protocol.Envelope, resp *protocol.Envelope) string {
	if resp.T != protocol.MsgOK {
		return ""
	}
	switch env.T {
	case protocol.MsgMigrate:
		if env.Migrate != nil && resp.Snapshot != nil {
			return fmt.Sprintf(" migrate: %d items, %d chars", len(env.Migrate.Items), len(env.Migrate.Characters))
		}
	case protocol.MsgSettle:
		if resp.Snapshot != nil {
			return fmt.Sprintf(" settle: run=%s outcome=%d rows=%d", env.RunID, env.Outcome, len(resp.Snapshot.Items))
		}
	case protocol.MsgPull:
		if resp.Snapshot != nil {
			return fmt.Sprintf(" pull: rows=%d chars=%d", len(resp.Snapshot.Items), len(resp.Snapshot.Characters))
		}
	}
	return ""
}

func (s *Server) dispatch(sess *session, env protocol.Envelope) protocol.Envelope {
	switch env.T {
	case protocol.MsgRegister:
		return s.doRegister(sess, env)
	case protocol.MsgAuth:
		return s.doAuth(sess, env)
	case protocol.MsgPull:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			state, err := s.Store.Load(context.Background(), sess.userID)
			if err != nil {
				return protocol.Envelope{}, err
			}
			return okEnv(env, state), nil
		})
	case protocol.MsgMigrate:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			if env.Migrate == nil {
				return protocol.Envelope{}, fmt.Errorf("missing migrate payload")
			}
			state, err := s.Store.Migrate(context.Background(), sess.userID, *env.Migrate, s.Items)
			if err != nil {
				return protocol.Envelope{}, err
			}
			return okEnv(env, state), nil
		})
	case protocol.MsgSettle:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			state, already, err := s.Store.ApplySettle(context.Background(), sess.userID, env.RunID, env.Outcome, env.Ops, s.Items)
			if err != nil {
				return protocol.Envelope{}, err
			}
			if already {
				log.Printf("[settle] run=%s user=%d idempotent replay (重复上报，回放首次结果)", env.RunID, sess.userID)
			} else {
				log.Printf("[settle] run=%s user=%d outcome=%d applied %d ops", env.RunID, sess.userID, env.Outcome, len(env.Ops))
			}
			return okEnv(env, state), nil
		})
	case protocol.MsgOps:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			state, err := s.Store.ApplyOps(context.Background(), sess.userID, env.Ops, s.Items)
			if err != nil {
				return protocol.Envelope{}, err
			}
			return okEnv(env, state), nil
		})
	default:
		return errEnv(env, protocol.ErrCodeBadMessage, "unknown message type "+env.T)
	}
}

func (s *Server) doRegister(sess *session, env protocol.Envelope) protocol.Envelope {
	userID, token, err := s.Store.Register(context.Background(), env.Device)
	if err != nil {
		return errEnv(env, protocol.ErrCodeRejected, err.Error())
	}
	sess.userID = userID
	state, err := s.Store.Load(context.Background(), userID)
	if err != nil {
		return errEnv(env, protocol.ErrCodeInternal, err.Error())
	}
	resp := okEnv(env, state)
	resp.Token = token
	return resp
}

func (s *Server) doAuth(sess *session, env protocol.Envelope) protocol.Envelope {
	userID, err := s.Store.Auth(context.Background(), env.Token)
	if err != nil {
		return errEnv(env, protocol.ErrCodeUnauthorized, "invalid token")
	}
	sess.userID = userID
	state, err := s.Store.Load(context.Background(), userID)
	if err != nil {
		return errEnv(env, protocol.ErrCodeInternal, err.Error())
	}
	return okEnv(env, state)
}

func (s *Server) requireAuth(sess *session, env protocol.Envelope, fn func() (protocol.Envelope, error)) protocol.Envelope {
	if sess.userID == 0 {
		return errEnv(env, protocol.ErrCodeUnauthorized, "login required")
	}
	resp, err := fn()
	if err != nil {
		return errEnv(env, classify(err), err.Error())
	}
	return resp
}

func classify(err error) string {
	switch {
	case errors.Is(err, core.ErrRejected), errors.Is(err, store.ErrAlreadyInitialized):
		return protocol.ErrCodeRejected
	case errors.Is(err, store.ErrUnknownToken):
		return protocol.ErrCodeUnauthorized
	default:
		return protocol.ErrCodeInternal
	}
}

func okEnv(req protocol.Envelope, state core.State) protocol.Envelope {
	snap := state.ToSnapshot()
	return protocol.Envelope{
		T:        protocol.MsgOK,
		V:        protocol.Version,
		RID:      req.RID,
		Snapshot: &snap,
	}
}

func errEnv(req protocol.Envelope, code, msg string) protocol.Envelope {
	return protocol.Envelope{T: protocol.MsgErr, V: protocol.Version, RID: req.RID, Code: code, Msg: msg}
}

func (s *Server) write(conn *ws.Conn, env protocol.Envelope) error {
	data, err := json.Marshal(env)
	if err != nil {
		return err
	}
	return conn.WriteMessage(data)
}

func (s *Server) writeErr(conn *ws.Conn, req protocol.Envelope, code, msg string) error {
	return s.write(conn, errEnv(req, code, msg))
}
