-- 仓库上云 v1 初始结构。
-- UTF-8 连接假设：dsn 请带 charset=utf8mb4,parseTime=true。

CREATE TABLE IF NOT EXISTS users (
    id          BIGINT NOT NULL AUTO_INCREMENT,
    device_uuid VARCHAR(64) NOT NULL,
    initialized TINYINT NOT NULL DEFAULT 0,
    created_at  TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uk_users_device (device_uuid)
) ENGINE = InnoDB;

CREATE TABLE IF NOT EXISTS auth_tokens (
    token      CHAR(32) NOT NULL,
    user_id    BIGINT NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMP NULL,
    PRIMARY KEY (token),
    KEY idx_tokens_user (user_id)
) ENGINE = InnoDB;

CREATE TABLE IF NOT EXISTS warehouse_items (
    uid        BIGINT NOT NULL AUTO_INCREMENT,
    user_id    BIGINT NOT NULL,
    item_id    INT NOT NULL,
    count      INT NOT NULL,
    x          INT NOT NULL DEFAULT 0,
    y          INT NOT NULL DEFAULT 0,
    is_rotated TINYINT NOT NULL DEFAULT 0,
    wearer     INT NOT NULL DEFAULT 0,
    PRIMARY KEY (uid),
    KEY idx_items_user (user_id)
) ENGINE = InnoDB;

CREATE TABLE IF NOT EXISTS user_characters (
    user_id      BIGINT NOT NULL,
    character_id INT NOT NULL,
    sort_order   INT NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, character_id)
) ENGINE = InnoDB;

-- runId 幂等：重复上报直接回放 snapshot_json；同时充当结算审计记录。
CREATE TABLE IF NOT EXISTS settlements (
    run_id       VARCHAR(64) NOT NULL,
    user_id      BIGINT NOT NULL,
    outcome      INT NOT NULL,
    snapshot_json MEDIUMTEXT NOT NULL,
    created_at   TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (run_id),
    KEY idx_settlements_user (user_id, created_at)
) ENGINE = InnoDB;
