-- warehouse_items 主键修正。
-- uid 铸号按用户独立（loadForUpdate 以 MAX(uid) WHERE user_id = ? 计 NextUID），
-- 但 PRIMARY KEY (uid) 是全局唯一：第二个持有物品的用户首次写入即与
-- 第一个用户的 uid 撞主键（1062），整个结算/操作事务回滚。
-- 改为 (user_id, uid) 复合主键：uid 仍是用户内稳定身份，客户端引用语义不变。
-- 一次性执行；0002_loadout.sql 之后运行。

-- 先去掉 AUTO_INCREMENT 属性，否则 DROP PRIMARY KEY 会因自增列不在索引首位被拒绝。
ALTER TABLE warehouse_items MODIFY uid BIGINT NOT NULL;

ALTER TABLE warehouse_items
    DROP PRIMARY KEY,
    ADD PRIMARY KEY (user_id, uid),
    DROP KEY idx_items_user;
-- idx_items_user (user_id) 已被新主键的最左前缀完全覆盖，一并移除。
