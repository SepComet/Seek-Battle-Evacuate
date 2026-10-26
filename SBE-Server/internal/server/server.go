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
	"sbe-server/internal/run"
	"sbe-server/internal/snowflake"
	"sbe-server/internal/store"
	"sbe-server/internal/ws"
)

// ridCacheSize 每连接保留的最近请求应答数，用于消息重试去重。
const ridCacheSize = 64

// historyLimit history 消息返回的最大战绩条数。
const historyLimit = 100

// Server 应用服务器。
type Server struct {
	Store  store.Store
	Items  core.ItemTable
	Limits core.Limits
	// Run 探索局会话管理器。为 nil 时 start_run/encounter/loot 返回 internal 错误
	// （测试里的纯经济用例不需要生成器配置，因此这里是可空装配点）。
	Run *run.Manager
}

// New 创建应用服务器（内存存储；生产部署见 cmd/sbe-server 的 mysql 变体）。
func New(items core.ItemTable, limits core.Limits) *Server {
	node, err := snowflake.New(0)
	if err != nil {
		panic(fmt.Sprintf("server: snowflake init: %v", err))
	}
	return &Server{Store: memorystore.New(node.Next), Items: items, Limits: limits}
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
			return fmt.Sprintf(" migrate: %d items, %d chars, party=%d", len(env.Migrate.Items), len(env.Migrate.Characters), len(env.Migrate.Party))
		}
	case protocol.MsgSettle:
		if resp.Snapshot != nil {
			return fmt.Sprintf(" settle: run=%s outcome=%d diff=%d rows=%d", env.RunID, env.Outcome, env.Difficulty, len(resp.Snapshot.Items))
		}
	case protocol.MsgPull:
		if resp.Snapshot != nil {
			return fmt.Sprintf(" pull: rows=%d chars=%d party=%d", len(resp.Snapshot.Items), len(resp.Snapshot.Characters), len(resp.Snapshot.Party))
		}
	case protocol.MsgHistory:
		return fmt.Sprintf(" history: %d runs", len(resp.History))
	case protocol.MsgStartRun:
		return fmt.Sprintf(" start_run: run=%s seed=%d diff=%d resume=%t consumed=%d spawn=(%g,%g) extraction=(%g,%g)",
			resp.RunID, resp.Seed, env.Difficulty, env.Resume, len(resp.Consumed),
			resp.SpawnPoint.X, resp.SpawnPoint.Y, resp.ExtractionPoint.X, resp.ExtractionPoint.Y)
	case protocol.MsgEncounter:
		return fmt.Sprintf(" encounter: run=%s point=%s party=%d threat=%d",
			env.RunID, env.PointID, resp.EnemyPartyID, resp.Threat)
	case protocol.MsgLoot:
		return fmt.Sprintf(" loot: run=%s point=%s items=%d %v", env.RunID, env.PointID, len(resp.ItemIDs), resp.ItemIDs)
	}
	return ""
}

func (s *Server) dispatch(sess *session, env protocol.Envelope) protocol.Envelope {
	switch env.T {
	case protocol.MsgRegister:
		return s.doRegister(sess, env)
	case protocol.MsgLogin:
		return s.doLogin(sess, env)
	case protocol.MsgAuth:
		return s.doAuth(sess, env)
	case protocol.MsgPing:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			return protocol.Envelope{T: protocol.MsgOK, V: protocol.Version, RID: env.RID}, nil
		})
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
			state, err := s.Store.Migrate(context.Background(), sess.userID, *env.Migrate, s.Items, s.Limits)
			if err != nil {
				return protocol.Envelope{}, err
			}
			return okEnv(env, state), nil
		})
	case protocol.MsgSettle:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			meta := core.SettleMeta{
				Outcome:    env.Outcome,
				Difficulty: env.Difficulty,
				Seed:       env.Seed,
				StartedAt:  env.StartedAt,
				EndedAt:    env.EndedAt,
			}
			// 对账（契约 §7 settle）：带出物资 ⊆ 开箱所得 ∪ 携带入场 ∪ 战斗所得。
			// 携带入场取服务器侧快照的 Carried（不是客户端上报），开箱所得来自本局消耗记录。
			if err := s.reconcileSettle(sess.userID, env); err != nil {
				return protocol.Envelope{}, err
			}
			state, already, err := s.Store.ApplySettle(context.Background(), sess.userID, env.RunID, meta, env.Ops, s.Items, s.Limits)
			if err != nil {
				return protocol.Envelope{}, err
			}
			if already {
				log.Printf("[settle] run=%s user=%d idempotent replay (重复上报，回放首次结果)", env.RunID, sess.userID)
			} else {
				log.Printf("[settle] run=%s user=%d outcome=%d applied %d ops", env.RunID, sess.userID, env.Outcome, len(env.Ops))
				// settle 是唯一的终结路径：只在真正落地后清除活跃 run。
				if s.Run != nil {
					s.Run.Finish(sess.userID, env.RunID)
				}
			}
			return okEnv(env, state), nil
		})
	case protocol.MsgStartRun:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			manager, err := s.runManager()
			if err != nil {
				return protocol.Envelope{}, err
			}
			view, err := manager.Start(sess.userID, env.Difficulty, env.Resume)
			if err != nil {
				return protocol.Envelope{}, err
			}
			resp := protocol.Envelope{
				T:               protocol.MsgOK,
				V:               protocol.Version,
				RID:             env.RID,
				RunID:           view.RunID,
				Seed:            view.Seed,
				RunDifficulty:   view.Difficulty,
				SpawnPoint:      &protocol.Vec2{X: view.Spawn.X, Y: view.Spawn.Y},
				ExtractionPoint: &protocol.Vec2{X: view.Extraction.X, Y: view.Extraction.Y},
				Consumed:        view.Consumed,
				Looted:          lootRecords(view.Looted),
			}
			if env.Resume && len(view.Consumed) > 0 {
				log.Printf("[run] run=%s user=%d resumed with %d consumed points", view.RunID, sess.userID, len(view.Consumed))
			}
			return resp, nil
		})
	case protocol.MsgEncounter:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			manager, err := s.runManager()
			if err != nil {
				return protocol.Envelope{}, err
			}
			partyID, threat, err := manager.Encounter(sess.userID, env.RunID, env.PointID)
			if err != nil {
				return protocol.Envelope{}, err
			}
			return protocol.Envelope{
				T:            protocol.MsgOK,
				V:            protocol.Version,
				RID:          env.RID,
				EnemyPartyID: partyID,
				Threat:       threat,
			}, nil
		})
	case protocol.MsgLoot:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			manager, err := s.runManager()
			if err != nil {
				return protocol.Envelope{}, err
			}
			itemIDs, err := manager.Loot(sess.userID, env.RunID, env.PointID)
			if err != nil {
				return protocol.Envelope{}, err
			}
			return protocol.Envelope{
				T:       protocol.MsgOK,
				V:       protocol.Version,
				RID:     env.RID,
				ItemIDs: itemIDs,
			}, nil
		})
	case protocol.MsgOps:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			state, err := s.Store.ApplyOps(context.Background(), sess.userID, env.Ops, s.Items, s.Limits)
			if err != nil {
				return protocol.Envelope{}, err
			}
			return okEnv(env, state), nil
		})
	case protocol.MsgHistory:
		return s.requireAuth(sess, env, func() (protocol.Envelope, error) {
			runs, err := s.Store.History(context.Background(), sess.userID, historyLimit)
			if err != nil {
				return protocol.Envelope{}, err
			}
			return protocol.Envelope{
				T:       protocol.MsgOK,
				V:       protocol.Version,
				RID:     env.RID,
				History: runs,
			}, nil
		})
	default:
		return errEnv(env, protocol.ErrCodeBadMessage, "unknown message type "+env.T)
	}
}

// runManager 取探索 run 管理器；未装配时返回错误（cmd/sbe-server 装配，测试可注入）。
func (s *Server) runManager() (*run.Manager, error) {
	if s.Run == nil {
		return nil, fmt.Errorf("server: exploration run manager is not configured")
	}
	return s.Run, nil
}

// reconcileSettle 结算额度对账（契约 §7 settle）：
// 带出物资（本批 add 按 itemId 汇总）⊆ 开箱所得 ∪ 携带入场（服务器快照的 Carried）∪ 战斗所得。
//
// 该用户有活跃 run 时必须 runId 匹配才放行（谎报其他 runId 等于绕过开箱对账，直接拒绝；
// 协议 v4 无旧客户端，不存在需要兼容的补报路径）；无活跃 run 跳过（幂等回放/离线结算）；
// 未装配 run 管理器时同样跳过。
func (s *Server) reconcileSettle(userID int64, env protocol.Envelope) error {
	if s.Run == nil || !s.Run.HasAnyActive(userID) {
		log.Printf("[settle-recon] user=%d run=%s skipped: no active run", userID, env.RunID)
		return nil
	}
	if !s.Run.Active(userID, env.RunID) {
		log.Printf("[settle-recon] user=%d run=%s rejected: runId does not match the active run", userID, env.RunID)
		return fmt.Errorf("%w: runId %s does not match the active run", core.ErrRejected, env.RunID)
	}
	// 携带入场额度取服务器侧快照（不是客户端上报）；读不到就不放行，不做静态兜底。
	state, err := s.Store.Load(context.Background(), userID)
	if err != nil {
		return err
	}
	battleLoot := make([]core.Stack, len(env.BattleLoot))
	for i, stack := range env.BattleLoot {
		battleLoot[i] = core.Stack{ItemID: stack.ItemID, Count: stack.Count}
	}
	added := core.SumAdds(env.Ops)
	looted := s.Run.LootedSums(userID, env.RunID)
	err = s.Run.ValidateSettle(userID, env.RunID, battleLoot, added, state.Carried)
	// 对账明细一行：带出 vs 三项额度来源。拒绝与放行都打，便于对客户端漏报/账本漂移定位。
	log.Printf("[settle-recon] user=%d run=%s added=%v looted=%v carried=%v battle=%v -> %s",
		userID, env.RunID, added, looted, core.SumStacks(state.Carried), core.SumStacks(battleLoot), verdict(err))
	return err
}

// verdict 对账结果的简短标记（配合 [settle-recon] 一行日志）。
func verdict(err error) string {
	if err != nil {
		return "rejected"
	}
	return "ok"
}

// lootRecords 把会话层的开箱清单转成线上结构。
func lootRecords(records []run.LootRecord) []protocol.LootRecord {
	out := make([]protocol.LootRecord, len(records))
	for i, record := range records {
		out[i] = protocol.LootRecord{PointID: record.PointID, ItemIDs: record.ItemIDs}
	}
	return out
}

// doRegister 注册账号：成功即视为已登录，回包带新 token 与 uid。
func (s *Server) doRegister(sess *session, env protocol.Envelope) protocol.Envelope {
	uid, token, err := s.Store.Register(context.Background(), env.Username, env.Password)
	if err != nil {
		return errEnv(env, classify(err), err.Error())
	}
	return s.afterLogin(sess, env, uid, token)
}

// doLogin 账号密码登录：成功即视为已登录，回包带新 token 与 uid。
func (s *Server) doLogin(sess *session, env protocol.Envelope) protocol.Envelope {
	uid, token, err := s.Store.Login(context.Background(), env.Username, env.Password)
	if err != nil {
		return errEnv(env, classify(err), err.Error())
	}
	return s.afterLogin(sess, env, uid, token)
}

// afterLogin 注册/登录成功后的公共尾段：加载状态、下发身份。
func (s *Server) afterLogin(sess *session, env protocol.Envelope, uid int64, token string) protocol.Envelope {
	sess.userID = uid
	state, err := s.Store.Load(context.Background(), uid)
	if err != nil {
		return errEnv(env, protocol.ErrCodeInternal, err.Error())
	}
	resp := okEnv(env, state)
	resp.Token = token
	resp.UserID = uid
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
	resp := okEnv(env, state)
	resp.UserID = userID
	return resp
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
	case errors.Is(err, store.ErrUsernameTaken):
		return protocol.ErrCodeConflict
	case errors.Is(err, store.ErrInvalidCredentials), errors.Is(err, store.ErrUnknownToken):
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
