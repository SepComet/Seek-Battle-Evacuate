package main

import (
	"encoding/json"
	"encoding/xml"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

// unity_verify：Editor 未运行时（Temp/UnityLockfile 不存在）用 batchmode 做
// 编译检查与 EditMode 测试。Editor 运行中一律拒绝，改走 mcp-for-unity。

type nunitCase struct {
	Name     string `xml:"name,attr"`
	FullName string `xml:"fullname,attr"`
	Result   string `xml:"result,attr"`
	Duration string `xml:"duration,attr"`
	Failure  *struct {
		Message    string `xml:"message"`
		StackTrace string `xml:"stack-trace"`
	} `xml:"failure"`
}

type nunitSuite struct {
	Result string       `xml:"result,attr"`
	Cases  []nunitCase  `xml:"test-case"`
	Suites []nunitSuite `xml:"test-suite"`
}

type nunitRun struct {
	Result       string       `xml:"result,attr"`
	Total        string       `xml:"total,attr"`
	Passed       string       `xml:"passed,attr"`
	Failed       string       `xml:"failed,attr"`
	Skipped      string       `xml:"skipped,attr"`
	Inconclusive string       `xml:"inconclusive,attr"`
	Duration     string       `xml:"duration,attr"`
	Cases        []nunitCase  `xml:"test-case"`
	Suites       []nunitSuite `xml:"test-suite"`
}

func (r *nunitRun) allCases() []nunitCase {
	cases := append([]nunitCase{}, r.Cases...)
	var walk func(s nunitSuite)
	walk = func(s nunitSuite) {
		cases = append(cases, s.Cases...)
		for _, sub := range s.Suites {
			walk(sub)
		}
	}
	for _, s := range r.Suites {
		walk(s)
	}
	return cases
}

var unityCompileErrMarkers = []string{
	"error CS",
	"Compilation failed",
	"Scripts have compiler errors",
	"Aborting batchmode due to failure",
}

// readProjectUnityVersion 从 ProjectSettings/ProjectVersion.txt 取编辑器版本（如 2022.3.62f3c1）。
func readProjectUnityVersion(clientDir string) (string, error) {
	data, err := os.ReadFile(filepath.Join(clientDir, "ProjectSettings", "ProjectVersion.txt"))
	if err != nil {
		return "", fmt.Errorf("读不到 ProjectSettings/ProjectVersion.txt: %w", err)
	}
	for _, line := range strings.Split(string(data), "\n") {
		line = strings.TrimSpace(line)
		if strings.HasPrefix(line, "m_EditorVersion:") {
			v := strings.TrimSpace(strings.TrimPrefix(line, "m_EditorVersion:"))
			if v != "" {
				return v, nil
			}
		}
	}
	return "", errors.New("ProjectVersion.txt 中没有 m_EditorVersion 行")
}

// resolveUnityExe 依次尝试：显式参数、SBE_UNITY_PATH 环境变量、按项目版本在 Unity Hub 安装目录里找。
// 找不到时列出本机已装版本，便于调用方指定。
func resolveUnityExe(clientDir, explicit string) (string, string, error) {
	version, verErr := readProjectUnityVersion(clientDir)

	if explicit != "" {
		if info, err := os.Stat(explicit); err == nil && !info.IsDir() {
			return explicit, version, nil
		}
		return "", version, fmt.Errorf("unity_path 指定的文件不存在: %s", explicit)
	}
	if p := os.Getenv("SBE_UNITY_PATH"); p != "" {
		if info, err := os.Stat(p); err == nil && !info.IsDir() {
			return p, version, nil
		}
		return "", version, fmt.Errorf("SBE_UNITY_PATH 指定的文件不存在: %s", p)
	}

	hubRoots := []string{
		`C:\Program Files\Unity\Hub\Editor`,
		`D:\Program Files\Unity\Hub\Editor`,
		`D:\Unity\Hub\Editor`,
		`C:\Unity\Hub\Editor`,
	}
	installed := []string{}
	for _, root := range hubRoots {
		entries, err := os.ReadDir(root)
		if err != nil {
			continue
		}
		for _, e := range entries {
			if !e.IsDir() {
				continue
			}
			cand := filepath.Join(root, e.Name(), "Editor", "Unity.exe")
			if info, err := os.Stat(cand); err == nil && !info.IsDir() {
				installed = append(installed, cand)
				if verErr == nil && e.Name() == version {
					return cand, version, nil
				}
			}
		}
	}
	if verErr != nil {
		return "", "", fmt.Errorf("无法确定项目 Unity 版本（%v），且 SBE_UNITY_PATH 未设置；已装版本: %s",
			verErr, strings.Join(installed, ", "))
	}
	return "", "", fmt.Errorf("未找到 Unity %s 的 Unity.exe（已装版本: %s）；可通过 unity_path 参数指定",
		version, strings.Join(installed, ", "))
}

// runUnityCapture 执行 Unity 并返回合并输出与退出码；超时时退出码为 -1。
// doneFile 非空时启用「结果即收工」：该文件出现（测试/检查结论已落盘）后给 graceSec
// 秒宽限，进程仍未自然退出则主动 Kill 并照常返回——退出码不再代表结论，以落盘文件为准。
// earlyReap 标记本次是否因收尾挂起被 Kill。
func runUnityCapture(unityExe string, args []string, timeoutSec float64, doneFile string, graceSec float64) (output string, exitCode int, runErr error, earlyReap bool) {
	ctxDone := make(chan struct{})
	cmd := exec.Command(unityExe, args...)
	var buf strings.Builder
	cmd.Stdout = &buf
	cmd.Stderr = &buf
	if err := cmd.Start(); err != nil {
		return buf.String(), -1, err, false
	}
	done := make(chan error, 1)
	go func() { done <- cmd.Wait() }()
	timer := time.AfterFunc(time.Duration(timeoutSec*float64(time.Second)), func() {
		_ = cmd.Process.Kill()
		close(ctxDone)
	})
	defer timer.Stop()

	reaped := make(chan struct{})
	if doneFile != "" {
		go func() {
			ticker := time.NewTicker(500 * time.Millisecond)
			defer ticker.Stop()
			for {
				if _, err := os.Stat(doneFile); err == nil {
					select {
					case <-time.After(time.Duration(graceSec * float64(time.Second))):
						// Kill 失败说明进程已在宽限窗口内自然退出，不算提前收工。
						if killErr := cmd.Process.Kill(); killErr == nil {
							close(reaped)
						}
						return
					case <-ctxDone:
						return
					}
				}
				select {
				case <-ticker.C:
				case <-ctxDone:
					return
				}
			}
		}()
	}

	err := <-done
	exitCode = 0
	if err != nil {
		var exitErr *exec.ExitError
		if errors.As(err, &exitErr) {
			exitCode = exitErr.ExitCode()
		} else {
			return buf.String(), -1, err, false
		}
	}
	// 超时优先于提前收工报告：若全局超时先触发，结论按超时算。
	select {
	case <-ctxDone:
		return buf.String(), exitCode, fmt.Errorf("Unity 超时被终止（%.0f 秒）", timeoutSec), false
	default:
	}
	select {
	case <-reaped:
		return buf.String(), exitCode, nil, true
	default:
	}
	return buf.String(), exitCode, nil, false
}

// verifyVerdict 写入 SBE-Client/Temp/last-unity-verify.json 的结论快照。
// MCP 客户端超时丢响应时，调用方可直接读该文件取回最近一次 compile/tests 结论。
type verifyVerdict struct {
	Mode        string   `json:"mode"`
	FinishedAt  string   `json:"finishedAt"`
	DurationSec float64  `json:"durationSec"`
	Status      string   `json:"status"` // ok / test-failed / compile-error / timeout / inconclusive
	Summary     string   `json:"summary"`
	Total       string   `json:"total,omitempty"`
	Passed      string   `json:"passed,omitempty"`
	Failed      string   `json:"failed,omitempty"`
	Skipped     string   `json:"skipped,omitempty"`
	Failures    []string `json:"failures,omitempty"` // 失败用例 fullname（上限 50 个）
	EarlyReap   bool     `json:"earlyReap,omitempty"`
	LogPath     string   `json:"logPath"`
	ResultsPath string   `json:"resultsPath,omitempty"`
}

// writeVerifyVerdict 结论落盘到固定路径；返回路径，失败返回空串（不影响主输出）。
func writeVerifyVerdict(clientDir string, v verifyVerdict) string {
	data, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		return ""
	}
	path := filepath.Join(clientDir, "Temp", "last-unity-verify.json")
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return ""
	}
	if err := os.WriteFile(path, append(data, '\n'), 0o644); err != nil {
		return ""
	}
	return path
}

// grepCompileErrors 从 batchmode 日志中摘出编译错误行（去重保序）。
func grepCompileErrors(log string) []string {
	seen := map[string]bool{}
	var lines []string
	for _, line := range strings.Split(log, "\n") {
		line = strings.TrimRight(line, "\r")
		lower := strings.ToLower(line)
		hit := strings.Contains(lower, "error cs")
		if !hit {
			for _, m := range unityCompileErrMarkers[1:] {
				if strings.Contains(line, m) {
					hit = true
					break
				}
			}
		}
		if hit && !seen[line] {
			seen[line] = true
			lines = append(lines, line)
		}
	}
	return lines
}

// scanBatchmodeHangRisks 扫描测试源码中在 batchmode -nographics 下会永久挂起的
// yield 指令（无渲染循环 → WaitForEndOfFrame 永不返回）。返回 "文件:行: 内容" 列表。
func scanBatchmodeHangRisks(testsDir string) []string {
	var hits []string
	patterns := []string{"WaitForEndOfFrame"}
	_ = filepath.WalkDir(testsDir, func(path string, d os.DirEntry, err error) error {
		if err != nil || d.IsDir() || !strings.HasSuffix(path, ".cs") {
			return nil
		}
		data, err := os.ReadFile(path)
		if err != nil {
			return nil
		}
		for i, line := range strings.Split(string(data), "\n") {
			trimmed := strings.TrimSpace(line)
			if strings.HasPrefix(trimmed, "//") || strings.HasPrefix(trimmed, "*") {
				continue
			}
			for _, p := range patterns {
				if strings.Contains(line, p) {
					rel, _ := filepath.Rel(testsDir, path)
					hits = append(hits, fmt.Sprintf("%s:%d: %s", rel, i+1, trimmed))
				}
			}
		}
		return nil
	})
	return hits
}

// unityProcessRunning 用 tasklist 探测是否存在 Unity.exe 进程；探测失败时保守视为存在。
func unityProcessRunning() bool {
	out, err := exec.Command("tasklist", "/FI", "IMAGENAME eq Unity.exe", "/NH").CombinedOutput()
	if err != nil {
		return true
	}
	return strings.Contains(string(out), "Unity.exe")
}

// cleanStaleLockfileAfterRun batchmode 结束后清理残留锁：仅当 lockfile 存在且无任何
// Unity 进程时删除（Unity batchmode 退出不删锁是已知行为），避免卡住下次运行与 Editor 启动。
func cleanStaleLockfileAfterRun(sb *strings.Builder, lockPath string) {
	if _, err := os.Stat(lockPath); err != nil {
		return
	}
	if unityProcessRunning() {
		sb.WriteString("\n注意: 运行结束后 Temp/UnityLockfile 仍存在且检测到 Unity 进程——可能是编辑器刚被打开，未做处理。\n")
		return
	}
	if err := os.Remove(lockPath); err == nil {
		sb.WriteString("\n已清理本次 batchmode 残留的 Temp/UnityLockfile（运行结束无 Unity 进程，属已知退出残留）。\n")
	}
}

func toolUnityVerify(args map[string]any) (string, error) {
	mode := argString(args, "mode", "check")
	if mode != "check" && mode != "compile" && mode != "tests" {
		return "", fmt.Errorf("mode 仅支持 check / compile / tests，收到 %q", mode)
	}
	clientDir := filepath.Join(repoRoot, "SBE-Client")
	lockPath := filepath.Join(clientDir, "Temp", "UnityLockfile")
	lockExists := false
	if _, err := os.Stat(lockPath); err == nil {
		lockExists = true
	}

	var sb strings.Builder
	fmt.Fprintf(&sb, "Unity 校验（mode=%s）\n项目: %s\n", mode, clientDir)

	if mode == "check" {
		switch {
		case lockExists && unityProcessRunning():
			sb.WriteString("Editor 状态: 运行中（Temp/UnityLockfile 存在且有 Unity 进程）→ 编译/测试走 mcp-for-unity，勿用 batchmode。\n")
		case lockExists:
			sb.WriteString("Editor 状态: lockfile 存在但无 Unity 进程 → 疑似残留锁（batchmode 退出残留或异常退出），compile/tests 会自动清除后执行。\n")
		default:
			sb.WriteString("Editor 状态: 未运行 → mode=tests 一次完成编译检查+EditMode 测试（约 1~3 分钟）；只想查编译用 mode=compile。\n")
		}
		unityExe, version, resolveErr := resolveUnityExe(clientDir, argString(args, "unity_path", ""))
		if resolveErr != nil {
			fmt.Fprintf(&sb, "Unity 路径: 解析失败（%v）\n", resolveErr)
		} else {
			fmt.Fprintf(&sb, "项目版本: %s\nUnity 路径: %s\n", version, unityExe)
		}
		return sb.String(), nil
	}

	if lockExists {
		if unityProcessRunning() {
			return "", fmt.Errorf("Editor 正在运行（%s 存在且有 Unity 进程），batchmode 与其互斥；EditMode 测试改走 mcp-for-unity", lockPath)
		}
		if err := os.Remove(lockPath); err != nil {
			return "", fmt.Errorf("存在疑似残留的 %s 但删除失败（可能被占用）: %v；请人工确认后处理", lockPath, err)
		}
		sb.WriteString("检测到残留的 Temp/UnityLockfile（无 Unity 进程，属上次 batchmode 退出残留或异常退出），已清除。\n\n")
	}

	unityExe, version, err := resolveUnityExe(clientDir, argString(args, "unity_path", ""))
	if err != nil {
		return "", err
	}

	tmpDir, err := os.MkdirTemp("", "sbe-unity-verify-")
	if err != nil {
		return "", fmt.Errorf("创建临时目录失败: %w", err)
	}
	logPath := filepath.Join(tmpDir, "unity.log")

	var unityArgs []string
	switch mode {
	case "compile":
		timeoutSec := argNumber(args, "timeout_sec", 600)
		unityArgs = []string{
			"-batchmode", "-nographics",
			"-projectPath", clientDir,
			"-quit", "-logFile", logPath,
		}
		fmt.Fprintf(&sb, "项目版本: %s\nUnity 路径: %s\n正在以 batchmode 编译检查（热 Library 约 1~3 分钟）...\n", version, unityExe)
		start := time.Now()
		output, exitCode, runErr, _ := runUnityCapture(unityExe, unityArgs, timeoutSec, "", 0)
		elapsed := time.Since(start).Seconds()
		logText := output
		if data, err := os.ReadFile(logPath); err == nil {
			logText = string(data) + "\n" + output
		}
		errLines := grepCompileErrors(logText)
		fmt.Fprintf(&sb, "退出码: %d | 耗时 %.1fs\n", exitCode, elapsed)
		if runErr != nil {
			fmt.Fprintf(&sb, "进程级错误: %v\n", runErr)
		}
		verdictStatus, verdictSummary := "inconclusive", "未发现 error CS，但退出码异常——请人工查看日志确认。"
		switch {
		case len(errLines) > 0:
			verdictStatus, verdictSummary = "compile-error", fmt.Sprintf("编译失败（日志命中 %d 处错误标记）。", len(errLines))
		case runErr == nil && exitCode == 0:
			verdictStatus, verdictSummary = "ok", "编译通过（日志无 error CS）。"
		}
		if runErr != nil {
			verdictStatus = "timeout"
			verdictSummary = "编译检查超时被终止。"
		}
		fmt.Fprintf(&sb, "结论: %s\n", verdictSummary)
		if len(errLines) > 0 {
			sb.WriteString("== 编译错误（前 40 行）==\n")
			for i, l := range errLines {
				if i >= 40 {
					fmt.Fprintf(&sb, "...（其余 %d 行见日志）\n", len(errLines)-40)
					break
				}
				sb.WriteString(l + "\n")
			}
		}
		cleanStaleLockfileAfterRun(&sb, lockPath)
		verdictPath := writeVerifyVerdict(clientDir, verifyVerdict{
			Mode: mode, FinishedAt: time.Now().Format(time.RFC3339), DurationSec: elapsed,
			Status: verdictStatus, Summary: verdictSummary, LogPath: logPath,
		})
		fmt.Fprintf(&sb, "日志: %s\n", logPath)
		if verdictPath != "" {
			fmt.Fprintf(&sb, "结论快照: %s（MCP 响应超时可直接读该文件取回结论）\n", verdictPath)
		}
		return sb.String(), nil

	case "tests":
		if hits := scanBatchmodeHangRisks(filepath.Join(clientDir, "Assets", "GameMain", "Scripts", "Tests")); len(hits) > 0 {
			sb.WriteString("== batchmode 挂起风险（-nographics 无渲染循环，以下 yield 指令会永久等待）==\n")
			for _, h := range hits {
				sb.WriteString("  " + h + "\n")
			}
			sb.WriteString("请改用 yield return null（编辑器更新循环驱动）。测试仍会继续跑，但命中用例会挂到进程超时。\n\n")
		}
		timeoutSec := argNumber(args, "timeout_sec", 900)
		xmlPath := filepath.Join(tmpDir, "test-results.xml")
		unityArgs = []string{
			"-batchmode", "-nographics",
			"-projectPath", clientDir,
			"-runTests", "-testPlatform", "EditMode",
			"-testResults", xmlPath,
			"-logFile", logPath,
		}
		if f := argString(args, "test_filter", ""); f != "" {
			unityArgs = append(unityArgs, "-testFilter", f)
		}
		fmt.Fprintf(&sb, "项目版本: %s\nUnity 路径: %s\n正在以 batchmode 编译并跑 EditMode 测试（一次启动同时得出编译与测试结论，热 Library 约 1~3 分钟）...\n", version, unityExe)
		start := time.Now()
		output, exitCode, runErr, earlyReap := runUnityCapture(unityExe, unityArgs, timeoutSec, xmlPath, 10)
		elapsed := time.Since(start).Seconds()
		fmt.Fprintf(&sb, "退出码: %d | 耗时 %.1fs\n", exitCode, elapsed)
		if runErr != nil {
			fmt.Fprintf(&sb, "进程级错误: %v\n", runErr)
		}
		if earlyReap {
			fmt.Fprintf(&sb, "注意: 结果 XML 已写出但 Unity 收尾挂起，进程已被提前收工（退出码不代表测试结论，以结果 XML 为准）。\n")
		}

		logText := output
		if data, err := os.ReadFile(logPath); err == nil {
			logText = string(data) + "\n" + output
		}

		var run nunitRun
		xmlOK := false
		if data, err := os.ReadFile(xmlPath); err == nil {
			if err := xml.Unmarshal(data, &run); err == nil && run.Result != "" {
				xmlOK = true
			}
		}

		// -runTests 隐含编译：能跑出测试 XML 即编译已通过；日志命中错误标记则测试必然没跑。
		errLines := grepCompileErrors(logText)
		verdictFailures := []string{}
		if len(errLines) > 0 {
			verdict := "——测试未运行"
			if xmlOK {
				verdict = ""
			}
			fmt.Fprintf(&sb, "编译: 失败（日志命中 %d 处错误标记）%s\n== 编译错误（前 40 行）==\n", len(errLines), verdict)
			for i, l := range errLines {
				if i >= 40 {
					fmt.Fprintf(&sb, "...（其余 %d 行见日志）\n", len(errLines)-40)
					break
				}
				sb.WriteString(l + "\n")
			}
		} else {
			sb.WriteString("编译: 通过\n")
		}
		if xmlOK {
			fmt.Fprintf(&sb, "测试汇总: %s 执行, %s 通过, %s 失败, %s 跳过%s（用时 %ss）\n",
				run.Total, run.Passed, run.Failed, run.Skipped, inconclusiveSuffix(run.Inconclusive), run.Duration)
			var failedCases []nunitCase
			for _, tc := range run.allCases() {
				if tc.Result == "Failed" {
					failedCases = append(failedCases, tc)
					if len(verdictFailures) < 50 {
						verdictFailures = append(verdictFailures, tc.FullName)
					}
				}
			}
			if len(failedCases) > 0 {
				sb.WriteString("\n== 失败用例（最多列 10 个）==\n")
				for i, tc := range failedCases {
					if i >= 10 {
						fmt.Fprintf(&sb, "...（其余 %d 个失败用例见 XML）\n", len(failedCases)-10)
						break
					}
					fmt.Fprintf(&sb, "--- %s ---\n", tc.FullName)
					if tc.Failure != nil {
						msg := strings.TrimSpace(tc.Failure.Message)
						if len(msg) > 800 {
							msg = msg[:800] + "..."
						}
						if msg != "" {
							sb.WriteString(msg + "\n")
						}
						if st := tailLines(strings.TrimSpace(tc.Failure.StackTrace), 6); st != "" {
							sb.WriteString("at " + strings.ReplaceAll(st, "\n", "\n  ") + "\n")
						}
					}
				}
			}
			switch {
			case runErr == nil && exitCode == 0 && run.Failed == "0":
				sb.WriteString("\n全部通过。\n")
			case exitCode == 2:
				sb.WriteString("\n存在失败用例（退出码 2 = 测试未全绿）。\n")
			}
		} else if len(errLines) == 0 {
			sb.WriteString("未拿到可解析的测试结果 XML——日志未见编译错误，测试没跑起来（环境问题）。日志尾部：\n")
			sb.WriteString(tailLines(logText, 40) + "\n")
		}
		cleanStaleLockfileAfterRun(&sb, lockPath)
		verdictStatus, verdictSummary := "inconclusive", "测试没跑起来（未拿到结果 XML，日志也未见编译错误）。"
		switch {
		case len(errLines) > 0 && !xmlOK:
			verdictStatus, verdictSummary = "compile-error", fmt.Sprintf("编译失败，测试未运行（日志命中 %d 处错误标记）。", len(errLines))
		case xmlOK && run.Failed != "0":
			verdictStatus = "test-failed"
			verdictSummary = fmt.Sprintf("%s 执行, %s 通过, %s 失败。", run.Total, run.Passed, run.Failed)
		case xmlOK:
			verdictStatus = "ok"
			verdictSummary = fmt.Sprintf("%s 执行全部通过。", run.Total)
		case runErr != nil:
			verdictStatus = "timeout"
			verdictSummary = "测试超时被终止，未拿到结果 XML。"
		}
		verdictPath := writeVerifyVerdict(clientDir, verifyVerdict{
			Mode: mode, FinishedAt: time.Now().Format(time.RFC3339), DurationSec: elapsed,
			Status: verdictStatus, Summary: verdictSummary,
			Total: run.Total, Passed: run.Passed, Failed: run.Failed, Skipped: run.Skipped,
			Failures: verdictFailures, EarlyReap: earlyReap,
			LogPath: logPath, ResultsPath: xmlPath,
		})
		fmt.Fprintf(&sb, "日志: %s\n结果 XML: %s\n", logPath, xmlPath)
		if verdictPath != "" {
			fmt.Fprintf(&sb, "结论快照: %s（MCP 响应超时可直接读该文件取回结论）\n", verdictPath)
		}
		return sb.String(), nil
	}
	return "", fmt.Errorf("unreachable mode: %s", mode)
}

func inconclusiveSuffix(v string) string {
	if v != "" && v != "0" {
		return ", " + v + " 不确定"
	}
	return ""
}
