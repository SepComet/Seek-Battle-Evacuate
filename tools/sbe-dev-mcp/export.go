package main

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// toolLubanExport 复刻 SBE-Design/gen_cli.bat 的完整行为：
// 1) 读 path.txt 决定输出根；2) dotnet Luban.dll 导 C#（cs-bin，可清理输出目录）；
// 3) dotnet Luban.dll 导 json+bin（cleanUpOutputDir=false，因为 DataTables 混有 UGF 资源）；
// 4) 把 DataTables/*.json 归一化为 LF（bat/sh 脚本均内置等价步骤）；
// 5) 报告 git 视角下 Gen/DataTables 的变更。
func toolLubanExport(args map[string]any) (string, error) {
	designDir := filepath.Join(repoRoot, "SBE-Design")
	lubanDll := filepath.Join(designDir, "Tools", "Luban", "Luban.dll")
	confFile := filepath.Join(designDir, "luban.conf")
	customTemplates := filepath.Join(designDir, "CustomTemplates")
	// 与脚本一致：ASSET_ROOT 相对仓库根，而非 SBE-Client（pathValidator 仅为资源路径列校验提供根目录）。
	assetRoot := filepath.Join(repoRoot, "Assets", "GameMain", "Textures")

	for _, p := range []string{lubanDll, confFile, customTemplates} {
		if _, err := os.Stat(p); err != nil {
			return "", fmt.Errorf("缺少 Luban 运行要件 %s（Luban 工具链需先就位）", p)
		}
	}

	outputRoot, err := resolveOutputRoot(designDir)
	if err != nil {
		return "", err
	}
	genDir := filepath.Join(outputRoot, "Scripts", "Base", "Gen")
	dataDir := filepath.Join(outputRoot, "DataTables")
	if err := os.MkdirAll(genDir, 0o755); err != nil {
		return "", fmt.Errorf("创建 Gen 输出目录失败: %w", err)
	}
	if err := os.MkdirAll(dataDir, 0o755); err != nil {
		return "", fmt.Errorf("创建 DataTables 输出目录失败: %w", err)
	}

	dotnet, err := lookDotnet()
	if err != nil {
		return "", err
	}

	var sb strings.Builder

	// 第一步：C# 代码导出。Gen 目录只有生成代码，允许 Luban 清理陈旧输出。
	start := time.Now()
	_, err = runCaptured(designDir, 300, dotnet,
		lubanDll, "-t", "client", "-c", "cs-bin",
		"--conf", confFile,
		"--customTemplateDir", customTemplates,
		"-x", "cs-bin.outputCodeDir="+genDir,
		"-x", "pathValidator.rootDir="+assetRoot,
		"-x", "lineEnding=lf")
	if err != nil {
		return "", fmt.Errorf("Luban C# 导出失败（%.1fs）:\n%s", time.Since(start).Seconds(), tailLines(err.Error(), 60))
	}
	fmt.Fprintf(&sb, "[1/2] C# 代码导出 (cs-bin): OK (%.1fs)\n", time.Since(start).Seconds())

	// 第二步：数据导出。DataTables 混有 UGF 资源，禁止清理输出目录。
	start = time.Now()
	dataOutput, err := runCaptured(designDir, 300, dotnet,
		lubanDll, "-t", "client", "-d", "json", "-d", "bin",
		"--conf", confFile,
		"-x", "json.outputDataDir="+dataDir,
		"-x", "bin.outputDataDir="+dataDir,
		"-x", "pathValidator.rootDir="+assetRoot,
		"-x", "cleanUpOutputDir=false")
	if err != nil {
		return "", fmt.Errorf("Luban 数据导出失败（%.1fs）:\n%s\n--- 上一步输出尾部 ---\n%s",
			time.Since(start).Seconds(), tailLines(err.Error(), 60), tailLines(dataOutput, 20))
	}
	fmt.Fprintf(&sb, "[2/2] 数据导出 (json+bin): OK (%.1fs)\n", time.Since(start).Seconds())

	normalized, changed := normalizeJsonToLF(dataDir)
	fmt.Fprintf(&sb, "LF 归一化: 处理 %d 个 json，改写 %d 个\n", normalized, changed)

	changedFiles, gitErr := gitStatusOf(repoRoot, genDir, dataDir)
	if gitErr != nil {
		fmt.Fprintf(&sb, "Git 变更: （跳过——%v）\n", gitErr)
	} else if len(changedFiles) == 0 {
		sb.WriteString("Git 变更: 无（导出结果与工作区一致）\n")
	} else {
		fmt.Fprintf(&sb, "Git 变更（%d 个文件）:\n", len(changedFiles))
		for _, f := range changedFiles {
			sb.WriteString("  " + f + "\n")
		}
	}

	if tail := tailLines(dataOutput, 10); tail != "" {
		sb.WriteString("--- Luban 数据导出输出尾部 ---\n" + tail + "\n")
	}
	return sb.String(), nil
}

// resolveOutputRoot 与脚本一致：优先 LUBAN_OUTPUT_ROOT 环境变量，否则取 path.txt 首行；
// 绝对路径直用，相对路径相对 SBE-Design 解析。
func resolveOutputRoot(designDir string) (string, error) {
	input := os.Getenv("LUBAN_OUTPUT_ROOT")
	if input == "" {
		raw, err := os.ReadFile(filepath.Join(designDir, "path.txt"))
		if err != nil {
			return "", fmt.Errorf("读取 path.txt 失败: %w", err)
		}
		line := strings.TrimSpace(strings.SplitN(string(raw), "\n", 2)[0])
		line = strings.TrimRight(line, "\r")
		if line == "" {
			return "", fmt.Errorf("path.txt 首行为空，无法确定输出目录")
		}
		input = line
	}
	if strings.HasPrefix(input, "/") || filepath.IsAbs(input) || (len(input) >= 2 && input[1] == ':') {
		return filepath.Abs(input)
	}
	return filepath.Abs(filepath.Join(designDir, input))
}

// normalizeJsonToLF 把 dataDir 下所有 *.json（忽略 .meta）中的 \r 移除，写回无 BOM UTF-8。
// 与 bat 的 powershell 步骤语义一致。
func normalizeJsonToLF(dataDir string) (processed int, changed int) {
	entries, err := os.ReadDir(dataDir)
	if err != nil {
		return 0, 0
	}
	for _, e := range entries {
		if e.IsDir() || !strings.HasSuffix(strings.ToLower(e.Name()), ".json") {
			continue
		}
		processed++
		full := filepath.Join(dataDir, e.Name())
		content, err := os.ReadFile(full)
		if err != nil {
			continue
		}
		if !bytesContainsRune(content) {
			continue
		}
		cleaned := strings.ReplaceAll(string(content), "\r", "")
		if err := os.WriteFile(full, []byte(cleaned), 0o644); err != nil {
			continue
		}
		changed++
	}
	return processed, changed
}

func bytesContainsRune(b []byte) bool {
	for _, c := range b {
		if c == '\r' {
			return true
		}
	}
	return false
}

// gitStatusOf 返回两个目录下发生变更的文件（porcelain 格式，仓库相对路径）。
func gitStatusOf(root string, dirs ...string) ([]string, error) {
	git, err := lookGit()
	if err != nil {
		return nil, err
	}
	relDirs := make([]string, 0, len(dirs))
	for _, d := range dirs {
		rel, err := filepath.Rel(root, d)
		if err != nil {
			return nil, err
		}
		relDirs = append(relDirs, filepath.ToSlash(rel))
	}
	output, err := runCaptured(root, 60, git, append([]string{"status", "--porcelain", "--"}, relDirs...)...)
	if err != nil {
		return nil, fmt.Errorf("git status 失败: %v", err)
	}
	var files []string
	for _, line := range strings.Split(output, "\n") {
		line = strings.TrimRight(line, "\r\n ")
		if line == "" {
			continue
		}
		// porcelain 格式：XY <path>，path 可能带引号（含中文时）。
		if len(line) > 3 {
			files = append(files, line)
		}
	}
	return files, nil
}

func tailLines(s string, n int) string {
	s = strings.TrimRight(s, "\n")
	if s == "" {
		return ""
	}
	lines := strings.Split(s, "\n")
	if len(lines) > n {
		lines = lines[len(lines)-n:]
	}
	return strings.Join(lines, "\n")
}
