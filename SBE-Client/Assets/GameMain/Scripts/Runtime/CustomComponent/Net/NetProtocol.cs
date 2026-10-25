using System.Collections.Generic;

namespace SepCore.Net
{
    /// <summary>
    /// 网络消息类型，与服务端 internal/protocol 一致。
    /// </summary>
    public static class NetMsg
    {
        public const int Version = 1;

        public const string Register = "register";
        public const string Auth = "auth";
        public const string Pull = "pull";
        public const string Migrate = "migrate";
        public const string Settle = "settle";
        public const string Ops = "ops";

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
    /// 服务器权威经济快照。
    /// </summary>
    [System.Serializable]
    public class NetSnapshot
    {
        public List<NetSnapRow> items = new List<NetSnapRow>();
        public List<NetSnapChar> characters = new List<NetSnapChar>();
        public bool initialized;
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
        public string device;
        public string token;
        public string runId;
        public int outcome;
        public List<NetOp> ops;
        public NetMigratePayload migrate;
        public NetSnapshot snapshot;
        public string code;
        public string msg;

        public static NetEnvelope Create(string type)
        {
            return new NetEnvelope { t = type, v = NetMsg.Version };
        }

        public bool IsOk => t == NetMsg.OK;
    }
}
