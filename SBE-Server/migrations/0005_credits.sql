-- 0005: 账号信用点余额（协议 v5）。
-- 余额由服务器在注册时按 tbglobalconfig.initialCredits 发放（Go 侧写值，非列默认值），
-- 客户端不上传余额；本阶段除注册发放外无其他变动路径（商城买卖随 P1-M3 接入）。
-- 存量账号回填与 initialCredits 相同的初值；回填值不随日后配置调整追溯。
ALTER TABLE users ADD COLUMN credits BIGINT NOT NULL DEFAULT 0;
UPDATE users SET credits = 1000 WHERE credits = 0;
