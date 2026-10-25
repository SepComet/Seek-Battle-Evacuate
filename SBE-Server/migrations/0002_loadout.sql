-- 局外状态上云 v2：战备配置（出战队列/难度/携带计划）与战绩元数据。
-- 一次性执行；0001_init.sql 之后运行。

CREATE TABLE IF NOT EXISTS user_loadout (
    user_id      BIGINT NOT NULL,
    difficulty   INT NOT NULL DEFAULT 0,
    party_json   TEXT NOT NULL,
    carried_json TEXT NOT NULL,
    PRIMARY KEY (user_id)
) ENGINE = InnoDB;

ALTER TABLE settlements
    ADD COLUMN difficulty INT NOT NULL DEFAULT 0,
    ADD COLUMN seed BIGINT NOT NULL DEFAULT 0,
    ADD COLUMN started_at BIGINT NOT NULL DEFAULT 0,
    ADD COLUMN ended_at BIGINT NOT NULL DEFAULT 0;
