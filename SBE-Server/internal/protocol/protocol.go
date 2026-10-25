// Package protocol 定义客户端与服务器之间 WebSocket 消息的线上结构（wire format）。
// 所有消息共用一个信封：{"t":类型,"v":协议版本,"rid":请求关联ID,...}。
// 响应统一为 {"t":"ok","rid":...,"token":...,"snapshot":...} 或 {"t":"err","rid":...,"code":...,"msg":...}。
package protocol

// Version 协议版本号，随消息携带，为将来灰度升级预留。
const Version = 1

// 消息类型。
const (
	MsgRegister = "register" // 注册匿名设备账号，body: device
	MsgAuth     = "auth"     // 凭 token 认证，body: token
	MsgPull     = "pull"     // 拉取全量经济快照
	MsgMigrate  = "migrate"  // 首登迁移/初始化，body: migrate
	MsgSettle   = "settle"   // 局结算对账（幂等），body: runId, outcome, ops
	MsgOps      = "ops"      // 大厅零散操作批次（穿卸/布局/消耗），body: ops

	MsgOK  = "ok"
	MsgErr = "err"
)

// 错误码。
const (
	ErrCodeBadMessage   = "bad_message"  // 消息格式或版本不合法
	ErrCodeUnauthorized = "unauthorized" // 未登录或 token 失效
	ErrCodeRejected     = "rejected"     // 操作校验失败（物品不存在、堆叠超限等）
	ErrCodeInternal     = "internal"     // 服务器内部错误
)

// OpKind 操作类型。
const (
	OpAdd     = "add"     // 新增物品行（战利品/新铸造），由服务器铸 UID；tid 为批内临时引用 ID
	OpDel     = "del"     // 删除/扣减已有行：count=0 表示整行删除，否则按数量扣减
	OpMov     = "mov"     // 仓库网格内已有行移动/旋转（含携带品归位）
	OpRel     = "rel"     // 穿戴行卸下回仓库网格（wearer 清零并落位）
	OpWear    = "wear"    // 穿上装备：引用 uid / tid / itemId 三选一
	OpRecruit = "recruit" // 花名册新增角色
)

// Op 经济操作。字段按操作类型取用，其余留零。
type Op struct {
	K string `json:"k"`
	// TID 批内临时引用 ID，add 时必填且批内唯一；wear 可引用。
	TID int `json:"tid,omitempty"`
	// UID 服务器铸造的行唯一 ID。
	UID int64 `json:"uid,omitempty"`
	// ItemID 物品配置 ID，对应 Luban tbitemconfig 主键。
	ItemID int `json:"itemId,omitempty"`
	// Count 数量；del 为 0 表示整行。
	Count int `json:"count,omitempty"`
	X     int `json:"x,omitempty"`
	Y     int `json:"y,omitempty"`
	// R 是否顺时针旋转 90 度。
	R bool `json:"r,omitempty"`
	// CID 角色 ID。
	CID int `json:"cid,omitempty"`
	// Slot 装备槽：1=武器，2=防具，与客户端 EquipmentSlotType 一致。
	Slot int `json:"slot,omitempty"`
}

// MigrateItem 迁移上传的仓库物品（无 UID，服务器统一铸造）。
type MigrateItem struct {
	ItemID int  `json:"itemId"`
	Count  int  `json:"count"`
	X      int  `json:"x"`
	Y      int  `json:"y"`
	R      bool `json:"r"`
}

// MigrateChar 迁移上传的角色（含穿戴装备的物品 ID，0 表示空栏）。
type MigrateChar struct {
	CID          int `json:"cid"`
	WeaponItemID int `json:"weaponItemId"`
	ArmorItemID  int `json:"armorItemId"`
}

// MigratePayload 迁移/初始化上传体。
type MigratePayload struct {
	Items      []MigrateItem `json:"items"`
	Characters []MigrateChar `json:"characters"`
}

// SnapRow 快照中的物品行。
type SnapRow struct {
	UID    int64 `json:"uid"`
	ItemID int   `json:"itemId"`
	Count  int   `json:"count"`
	X      int   `json:"x"`
	Y      int   `json:"y"`
	R      bool  `json:"r"`
	// Wearer 穿戴者角色 ID；0 表示位于仓库网格。
	Wearer int `json:"wearer"`
}

// SnapChar 快照中的花名册条目（保持顺序，顺序即入队次序）。
type SnapChar struct {
	CID int `json:"cid"`
}

// Snapshot 全量经济快照，服务器为唯一真源。
type Snapshot struct {
	Items       []SnapRow  `json:"items"`
	Characters  []SnapChar `json:"characters"`
	Initialized bool       `json:"initialized"`
}

// Envelope 消息信封。
type Envelope struct {
	T   string `json:"t"`
	V   int    `json:"v,omitempty"`
	RID int64  `json:"rid,omitempty"`
	// Device register 时的设备 UUID。
	Device string `json:"device,omitempty"`
	// Token auth 时的令牌；ok 响应时为下发的新令牌。
	Token   string          `json:"token,omitempty"`
	RunID   string          `json:"runId,omitempty"`
	Outcome int             `json:"outcome,omitempty"`
	Ops     []Op            `json:"ops,omitempty"`
	Migrate *MigratePayload `json:"migrate,omitempty"`
	// Snapshot ok 响应携带的权威快照。
	Snapshot *Snapshot `json:"snapshot,omitempty"`
	// Code/Msg err 响应的错误码与说明。
	Code string `json:"code,omitempty"`
	Msg  string `json:"msg,omitempty"`
}
