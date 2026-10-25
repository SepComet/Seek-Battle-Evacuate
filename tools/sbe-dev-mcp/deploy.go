package main

import (
	"fmt"
	"path/filepath"
	"strings"
	"time"
)

// lookDocker 解析 docker CLI（Docker Desktop 装机路径兜底，引擎随 Desktop 启动）。
func lookDocker() (string, error) {
	return lookExecutable("docker",
		`C:\Program Files\Docker\Docker\resources\bin\docker.exe`,
	)
}

// toolDeploy 一键部署：docker compose up -d --build。
// 默认只重建 sbe-server 并 --force-recreate（规避"镜像 tag 相同但运行中容器不换新"的坑）；
// service=all 时重建整栈（首次部署用，不做 force-recreate）。
// 默认等待 sbe-server 的监听日志，随后附容器状态与近期日志。
func toolDeploy(args map[string]any) (string, error) {
	dockerExe, err := lookDocker()
	if err != nil {
		return "", err
	}
	serverDir := filepath.Join(repoRoot, "SBE-Server")
	service := argString(args, "service", "sbe-server")
	timeoutSec := argNumber(args, "timeout_sec", 600)
	wait := argBoolDefault(args, "wait_listen", true)

	composeArgs := []string{"compose", "up", "-d", "--build"}
	if service != "all" {
		composeArgs = append(composeArgs, "--force-recreate", service)
	}

	start := time.Now()
	upOut, upErr := runCaptured(serverDir, timeoutSec, dockerExe, composeArgs...)
	elapsed := time.Since(start).Seconds()

	var sb strings.Builder
	if upErr != nil {
		fmt.Fprintf(&sb, "deploy 失败（%.1fs）\n", elapsed)
		sb.WriteString(tailLines(upOut, 30))
		combined := upOut + upErr.Error()
		if strings.Contains(combined, "error during connect") ||
			strings.Contains(combined, "cannot connect to the Docker daemon") {
			sb.WriteString("\n（Docker 引擎未运行——启动 Docker Desktop 等鲸鱼图标变绿后重试）\n")
		}
		sb.WriteString("\n-- 进程级错误 --\n" + upErr.Error() + "\n")
		return sb.String(), nil
	}
	fmt.Fprintf(&sb, "compose up 完成（%.1fs）\n", elapsed)

	if wait {
		listening := false
		for i := 0; i < 10; i++ {
			logs, logErr := runCaptured(serverDir, 30, dockerExe, "compose", "logs", "--since", "2m", "sbe-server")
			if logErr == nil && strings.Contains(logs, "listening on ws://") {
				listening = true
				break
			}
			time.Sleep(3 * time.Second)
		}
		if listening {
			sb.WriteString("服务器已监听 ✓\n")
		} else {
			sb.WriteString("警告：30 秒内未在 sbe-server 日志中看到 listening——用 compose logs 排查\n")
		}
	}

	psOut, psErr := runCaptured(serverDir, 30, dockerExe, "compose", "ps")
	if psErr == nil && strings.TrimSpace(psOut) != "" {
		sb.WriteString("\n== compose ps ==\n" + strings.TrimRight(psOut, "\n"))
	}
	logsOut, logErr := runCaptured(serverDir, 30, dockerExe, "compose", "logs", "--since", "2m", "sbe-server")
	if logErr == nil && strings.TrimSpace(logsOut) != "" {
		sb.WriteString("\n\n== sbe-server 近期日志 ==\n" + tailLines(strings.TrimRight(logsOut, "\n"), 10))
	}
	sb.WriteString("\n\n下一步：用 smoke 工具跑注册/ping/登录冒烟。\n")
	return sb.String(), nil
}
