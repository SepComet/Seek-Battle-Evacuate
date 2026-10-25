-- 账号密码体系（协议 v3）：users 表重建为雪花 uid 主键 + 用户名/口令哈希。
-- 已确认决策：旧设备匿名账号及其名下数据作废，auth_tokens 一并清空。
-- 注意：warehouse_items / user_characters / user_loadout / settlements 中
-- 旧账号数据成为孤儿行；正式部署建议直接使用新库，一次性执行；0003 之后运行。

DROP TABLE IF EXISTS users;
TRUNCATE TABLE auth_tokens;

CREATE TABLE users (
    uid           BIGINT NOT NULL,
    username      VARCHAR(32) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    initialized   TINYINT NOT NULL DEFAULT 0,
    created_at    TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (uid),
    UNIQUE KEY uk_users_username (username)
) ENGINE = InnoDB;
