// Package store 定义经济状态的持久化接口与错误。
// 实现方负责并发安全与事务性；核心逻辑在 internal/core（纯函数）。
package store

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"errors"

	"sbe-server/internal/core"
	"sbe-server/internal/protocol"
)

// ErrUnknownToken token 不存在或已过期。
var ErrUnknownToken = errors.New("store: unknown token")

// ErrAlreadyInitialized 账号已完成初始化，拒绝再次迁移。
var ErrAlreadyInitialized = errors.New("store: already initialized")

// NewToken 生成随机登录令牌（32 位十六进制）。
func NewToken() (string, error) {
	var raw [16]byte
	if _, err := rand.Read(raw[:]); err != nil {
		return "", err
	}
	return hex.EncodeToString(raw[:]), nil
}

// Store 经济状态存储接口。所有方法须线程安全。
type Store interface {
	// Register 以设备 UUID 注册（已存在则直接登录），返回新铸造的 token。
	Register(ctx context.Context, device string) (userID int64, token string, err error)

	// Auth 校验 token，返回所属用户。
	Auth(ctx context.Context, token string) (userID int64, err error)

	// Load 读取用户完整经济状态。
	Load(ctx context.Context, userID int64) (core.State, error)

	// ApplySettle 应用一次结算。runId 全局唯一：重复提交返回首次结果与 already=true。
	ApplySettle(ctx context.Context, userID int64, runID string, outcome int, ops []protocol.Op, items core.ItemTable) (state core.State, already bool, err error)

	// ApplyOps 应用一批大厅操作（无 runId 幂等，幂等由会话层 rid 缓存负责）。
	ApplyOps(ctx context.Context, userID int64, ops []protocol.Op, items core.ItemTable) (core.State, error)

	// Migrate 初始化用户状态；已初始化时返回 ErrAlreadyInitialized。
	Migrate(ctx context.Context, userID int64, payload protocol.MigratePayload, items core.ItemTable) (core.State, error)
}
