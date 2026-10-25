package main

import (
	"fmt"
	"path/filepath"
	"strings"
	"time"
)

// toolSmoke 对指定服务器跑注册/ping/登录冒烟（cmd/smoke），默认地址为容器栈映射的 127.0.0.1:7777。
func toolSmoke(args map[string]any) (string, error) {
	goExe, err := lookGo()
	if err != nil {
		return "", err
	}
	serverDir := filepath.Join(repoRoot, "SBE-Server")
	addr := argString(args, "addr", "127.0.0.1:7777")
	timeoutSec := argNumber(args, "timeout_sec", 120)

	start := time.Now()
	output, err := runCaptured(serverDir, timeoutSec, goExe, "run", "./cmd/smoke", "-addr", addr)
	elapsed := time.Since(start).Seconds()

	var sb strings.Builder
	if err == nil && strings.Contains(output, "冒烟通过") {
		fmt.Fprintf(&sb, "冒烟通过（%.1fs）\n%s", elapsed, strings.TrimSpace(output))
		return sb.String(), nil
	}

	fmt.Fprintf(&sb, "冒烟失败（%.1fs）\n%s", elapsed, strings.TrimSpace(output))
	if err != nil {
		sb.WriteString("\n-- 进程级错误 --\n" + err.Error() + "\n")
	}
	sb.WriteString("\n提示：确认容器栈在运行（deploy 工具），地址默认 127.0.0.1:7777；" +
		"register 被拒时看返回的 code/msg（internal 通常是服务器版本与数据库 schema 不匹配，需重新 deploy）。\n")
	return sb.String(), nil
}
