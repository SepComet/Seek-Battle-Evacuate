using System.Collections.Generic;

namespace SepCore.Net
{
    /// <summary>
    /// 网络消息类型，与服务端 internal/protocol 一致。
    /// </summary>
    public static class NetMsg
    {
        // v3：账号体系改为用户名+密码（register=注册账号、login=账密登录），用户主键为雪花 uid。
        // v4：探索内容上云——新增 start_run/encounter/loot，settle 增补 battleLoot；
        //     信封增加 run 会话字段（契约 exploration-online-v1 §7）。
        public const int Version = 4;

        public const string Register = "register";
        public const string Login = "login";
        public const string Auth = "auth";
        public const string Pull = "pull";
        public const string Migrate = "migrate";
        public const string Settle = "settle";
        public const string Ops = "ops";
        public const string History = "history";
        public const string Ping = "ping";

        /// <summary>开局：服务器分配种子并生成全图内容；幂等（返回活跃 run 现状）。</summary>
        public const string StartRun = "start_run";

        /// <summary>撞敌：校验敌点存在并返回队伍构成，body: runId, pointId。</summary>
        public const string Encounter = "encounter";

        /// <summary>开箱：校验点存在并标记消耗，body: runId, pointId。</summary>
        public const string Loot = "loot";

        public const string OK = "ok";
        public const string Err = "err";
    }

    /// <summary>
    /// 网络错误码。
    /// </summary>
    public static class NetErrCode
    {
        public const string BadMessage = "bad_message";
        public const string Unauthorized = "unauthorized";
        public const string Rejected = "rejected";
        public const string Conflict = "conflict";
        public const string Internal = "internal";
    }

    /// <summary>
    /// 操作类型。
    /// </summary>
    public static class NetOpKind
    {
        public const string Add = "add";
        public const string Del = "del";
        public const string Mov = "mov";
        public const string Rel = "rel";
        public const string Wear = "wear";
        public const string Recruit = "recruit";
        public const string Party = "party";
        public const string Difficulty = "difficulty";
        public const string Carry = "carry";
    }

    /// <summary>
    /// 装备槽位值，与 EquipmentSlotType 一致（1=武器 2=防具）。
    /// </summary>
    public static class NetSlot
    {
        public const int Weapon = 1;
        public const int Armor = 2;
    }

    /// <summary>
    /// 经济操作。字段按操作类型取用，其余留零。
    /// </summary>
    [System.Serializable]
    public class NetOp
    {
        public string k;
        public int tid;
        public long uid;
        public int itemId;
        public int count;
        public int x;
        public int y;
        public bool r;
        public int cid;
        public int slot;
        public int[] party;
        public int difficulty;
        public List<NetStack> carry;

        public static NetOp Add(int tid, int itemId, int count, int x, int y, bool rotated)
        {
            return new NetOp { k = NetOpKind.Add, tid = tid, itemId = itemId, count = count, x = x, y = y, r = rotated };
        }

        public static NetOp Del(long uid, int count = 0)
        {
            return new NetOp { k = NetOpKind.Del, uid = uid, count = count };
        }

        public static NetOp Mov(long uid, int x, int y, bool rotated)
        {
            return new NetOp { k = NetOpKind.Mov, uid = uid, x = x, y = y, r = rotated };
        }

        public static NetOp Rel(long uid, int x, int y, bool rotated)
        {
            return new NetOp { k = NetOpKind.Rel, uid = uid, x = x, y = y, r = rotated };
        }

        public static NetOp WearByUid(int cid, int slot, long uid)
        {
            return new NetOp { k = NetOpKind.Wear, cid = cid, slot = slot, uid = uid };
        }

        public static NetOp WearByItemId(int cid, int slot, int itemId)
        {
            return new NetOp { k = NetOpKind.Wear, cid = cid, slot = slot, itemId = itemId };
        }

        public static NetOp Recruit(int cid)
        {
            return new NetOp { k = NetOpKind.Recruit, cid = cid };
        }

        public static NetOp Party(int[] party)
        {
            return new NetOp { k = NetOpKind.Party, party = party };
        }

        public static NetOp Difficulty(int difficulty)
        {
            return new NetOp { k = NetOpKind.Difficulty, difficulty = difficulty };
        }

        public static NetOp Carry(List<NetStack> carry)
        {
            return new NetOp { k = NetOpKind.Carry, carry = carry };
        }
    }

    /// <summary>
    /// 无实例身份的物品堆（携带计划条目），与服务端 protocol.Stack 一致。
    /// </summary>
    [System.Serializable]
    public class NetStack
    {
        public int itemId;
        public int count;

        public NetStack()
        {
        }

        public NetStack(int itemId, int count)
        {
            this.itemId = itemId;
            this.count = count;
        }
    }

    /// <summary>
    /// 迁移上传的仓库物品。
    /// </summary>
    [System.Serializable]
    public class NetMigrateItem
    {
        public int itemId;
        public int count;
        public int x;
        public int y;
        public bool r;
    }

    /// <summary>
    /// 迁移上传的角色。
    /// </summary>
    [System.Serializable]
    public class NetMigrateChar
    {
        public int cid;
        public int weaponItemId;
        public int armorItemId;
    }

    /// <summary>
    /// 迁移上传体。
    /// </summary>
    [System.Serializable]
    public class NetMigratePayload
    {
        public List<NetMigrateItem> items = new List<NetMigrateItem>();
        public List<NetMigrateChar> characters = new List<NetMigrateChar>();

        // 战备配置（缺省 = 未配置）
        public int[] party;
        public int difficulty;
        public List<NetStack> carried = new List<NetStack>();
    }

    /// <summary>
    /// 快照中的物品行。
    /// </summary>
    [System.Serializable]
    public class NetSnapRow
    {
        public long uid;
        public int itemId;
        public int count;
        public int x;
        public int y;
        public bool r;
        public int wearer;
    }

    /// <summary>
    /// 快照中的花名册条目。
    /// </summary>
    [System.Serializable]
    public class NetSnapChar
    {
        public int cid;
    }

    /// <summary>
    /// 服务器权威快照。
    /// </summary>
    [System.Serializable]
    public class NetSnapshot
    {
        public List<NetSnapRow> items = new List<NetSnapRow>();
        public List<NetSnapChar> characters = new List<NetSnapChar>();
        public bool initialized;

        // 战备配置：空队列/0 难度/空携带 = 未配置
        public int[] party;
        public int difficulty;
        public List<NetStack> carried;
    }

    /// <summary>
    /// 战绩记录（history 响应条目），与服务端 protocol.SnapRun 一致。
    /// </summary>
    [System.Serializable]
    public class NetRunRecord
    {
        public string runId;
        public int outcome;
        public int difficulty;
        public long seed;
        public long startedAt;
        public long endedAt;
    }

    /// <summary>
    /// 瓦片坐标（start_run 下发的出生/撤离点），与服务端 protocol.Vec2 一致。
    /// </summary>
    [System.Serializable]
    public class NetVec2
    {
        public float x;
        public float y;

        public NetVec2()
        {
        }

        public NetVec2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }

    /// <summary>
    /// 一个已开箱点的物品清单（start_run 重连恢复用），与服务端 protocol.LootRecord 一致。
    /// </summary>
    [System.Serializable]
    public class NetLootRecord
    {
        public string pointId;
        public List<int> itemIds = new List<int>();
    }

    /// <summary>
    /// 消息信封，字段与服务端 protocol.Envelope 一致。
    /// </summary>
    [System.Serializable]
    public class NetEnvelope
    {
        public string t;
        public int v;
        public long rid;

        // register/login 携带的账号凭据
        public string username;
        public string password;
        public string token;
        public long userId;
        public string runId;
        public int outcome;
        public List<NetOp> ops;
        public NetMigratePayload migrate;

        // settle 携带的战绩元数据
        public int difficulty;
        public long seed;
        public long startedAt;
        public long endedAt;

        // history 响应携带的战绩列表
        public List<NetRunRecord> history;

        public NetSnapshot snapshot;
        public string code;
        public string msg;

        // ---- 探索 run（协议 v4，契约 §7）----

        /// <summary>start_run 请求：true 表示断线重连的恢复路径，不得误建新局。</summary>
        public bool resume;

        /// <summary>encounter/loot 请求的交互主键（"r&lt;i&gt;" / "e&lt;i&gt;"，契约 §6）。</summary>
        public string pointId;

        /// <summary>start_run 响应下发的出生点（显式值，不参与预测）。</summary>
        public NetVec2 spawnPoint;

        /// <summary>start_run 响应下发的撤离点（显式值，不参与预测）。</summary>
        public NetVec2 extractionPoint;

        /// <summary>start_run 响应下发的本局难度：幂等重入返回旧局现状，客户端据此发现难度漂移。</summary>
        public int runDifficulty;

        /// <summary>start_run 响应：已消耗点 ID，按开箱序（重连恢复用）。</summary>
        public List<string> consumed;

        /// <summary>start_run 响应：已开箱清单，与 <see cref="consumed"/> 同序（重连恢复用）。</summary>
        public List<NetLootRecord> looted;

        /// <summary>encounter 响应：本局该敌点的权威队伍。</summary>
        public int enemyPartyId;

        /// <summary>encounter 响应：本局该敌点的权威威胁等级。</summary>
        public int threat;

        /// <summary>loot 响应（以及 start_run 的 looted 条目内部）：掉落清单，顺序即掉落序。</summary>
        public List<int> itemIds;

        /// <summary>settle 请求：本局战斗所得（客户端上报，本阶段服务器信任）。</summary>
        public List<NetStack> battleLoot;

        public static NetEnvelope Create(string type)
        {
            return new NetEnvelope { t = type, v = NetMsg.Version };
        }

        public bool IsOk => t == NetMsg.OK;
    }
}
