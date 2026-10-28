// sbe-dev-mcp：SBE（Seek-Battle-Evacuate）开发工作流 MCP server（stdio）。
// 提供六个工具：luban_export（Luban 配置导出）、server_test（Go 服务端测试）、
// consistency_check（表名/生成文件头/asmdef 引用一致性预检）、
// deploy（Docker 栈一键部署）、smoke（服务器注册/ping/登录冒烟）、
// unity_verify（batchmode 编译检查与 EditMode 测试，Editor 未运行时）。
// 仅依赖 Go 标准库，构建：go build -o bin/sbe-dev-mcp.exe .
package main

import (
	"bufio"
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sync"
)

const serverName = "sbe-dev"
const serverVersion = "0.4.0"

const maxContentChars = 48000

type rpcRequest struct {
	JSONRPC string          `json:"jsonrpc"`
	ID      json.RawMessage `json:"id"`
	Method  string          `json:"method"`
	Params  json.RawMessage `json:"params"`
}

type rpcError struct {
	Code    int    `json:"code"`
	Message string `json:"message"`
}

type rpcResponse struct {
	JSONRPC string          `json:"jsonrpc"`
	ID      json.RawMessage `json:"id,omitempty"`
	Result  any             `json:"result,omitempty"`
	Error   *rpcError       `json:"error,omitempty"`
}

// 工具串行执行：Luban 导出与 go test 都是重活，避免并发互踩。
var toolMu sync.Mutex

var repoRoot string

func main() {
	repoRoot = resolveRepoRoot()
	if repoRoot == "" {
		fmt.Fprintln(os.Stderr, "[sbe-dev-mcp] 无法定位仓库根目录：请通过 SBE_REPO_ROOT 环境变量指定（应包含 AGENTS.md 与 SBE-Design）")
		os.Exit(1)
	}

	reader := bufio.NewReaderSize(os.Stdin, 1<<20)
	enc := json.NewEncoder(os.Stdout)
	for {
		line, err := reader.ReadBytes('\n')
		if trimmed := bytes.TrimSpace(line); len(trimmed) > 0 {
			handleLine(trimmed, enc)
		}
		if err != nil {
			if err != io.EOF {
				fmt.Fprintln(os.Stderr, "[sbe-dev-mcp] stdin 读取错误:", err)
			}
			return
		}
	}
}

func handleLine(line []byte, enc *json.Encoder) {
	var req rpcRequest
	if err := json.Unmarshal(line, &req); err != nil {
		writeError(enc, nil, -32700, "parse error: "+err.Error())
		return
	}
	if req.Method == "" {
		return
	}
	// 通知（无 id）不回复。
	if len(req.ID) == 0 || string(req.ID) == "null" {
		return
	}

	switch req.Method {
	case "initialize":
		var p struct {
			ProtocolVersion string `json:"protocolVersion"`
		}
		_ = json.Unmarshal(req.Params, &p)
		version := p.ProtocolVersion
		if version == "" {
			version = "2025-06-18"
		}
		writeResult(enc, req.ID, map[string]any{
			"protocolVersion": version,
			"capabilities":    map[string]any{"tools": map[string]any{"listChanged": false}},
			"serverInfo":      map[string]any{"name": serverName, "version": serverVersion},
			"instructions": "SBE 仓库开发工具：改动 SBE-Design 源表后用 luban_export 导出；服务端改动用 server_test 跑测试；" +
					"改表/改 UI/改程序集引用后、提交前用 consistency_check 做一致性预检；" +
					"客户端代码改完用 unity_verify 验证（mode=check 先确认 Editor 状态：Editor 未运行时 batchmode 编译/跑测，运行中改走 MCP for Unity）",
		})
	case "ping":
		writeResult(enc, req.ID, map[string]any{})
	case "tools/list":
		writeResult(enc, req.ID, map[string]any{"tools": toolDefinitions()})
	case "tools/call":
		var p struct {
			Name      string          `json:"name"`
			Arguments json.RawMessage `json:"arguments"`
		}
		if err := json.Unmarshal(req.Params, &p); err != nil {
			writeError(enc, req.ID, -32602, "invalid params: "+err.Error())
			return
		}
		tool, ok := findTool(p.Name)
		if !ok {
			writeError(enc, req.ID, -32602, "unknown tool: "+p.Name)
			return
		}
		var args map[string]any
		if len(p.Arguments) > 0 {
			if err := json.Unmarshal(p.Arguments, &args); err != nil {
				writeError(enc, req.ID, -32602, "invalid arguments: "+err.Error())
				return
			}
		}
		toolMu.Lock()
		text, toolErr := runToolSafely(tool, args)
		toolMu.Unlock()
		if toolErr != nil {
			writeResult(enc, req.ID, map[string]any{
				"content": []map[string]any{{"type": "text", "text": truncate(toolErr.Error(), maxContentChars)}},
				"isError": true,
			})
			return
		}
		writeResult(enc, req.ID, map[string]any{
			"content": []map[string]any{{"type": "text", "text": truncate(text, maxContentChars)}},
			"isError": false,
		})
	default:
		writeError(enc, req.ID, -32601, "method not found: "+req.Method)
	}
}

func runToolSafely(tool tool, args map[string]any) (text string, err error) {
	defer func() {
		if r := recover(); r != nil {
			err = fmt.Errorf("tool %s panicked: %v", tool.name, r)
		}
	}()
	return tool.fn(args)
}

func writeResult(enc *json.Encoder, id json.RawMessage, result any) {
	_ = enc.Encode(rpcResponse{JSONRPC: "2.0", ID: id, Result: result})
}

func writeError(enc *json.Encoder, id json.RawMessage, code int, msg string) {
	_ = enc.Encode(rpcResponse{JSONRPC: "2.0", ID: id, Error: &rpcError{Code: code, Message: msg}})
}

func truncate(s string, max int) string {
	if len(s) <= max {
		return s
	}
	return s[:max] + "\n...[输出已截断]"
}

// resolveRepoRoot 依次尝试：SBE_REPO_ROOT 环境变量、cwd 向上找（含 AGENTS.md + SBE-Design）、
// 可执行文件所在目录向上找。
func resolveRepoRoot() string {
	if v := os.Getenv("SBE_REPO_ROOT"); v != "" && isRepoRoot(v) {
		return v
	}
	for _, base := range []string{cwdOrEmpty(), executableDirOrEmpty()} {
		dir := base
		for dir != "" {
			if isRepoRoot(dir) {
				return dir
			}
			parent := filepath.Dir(dir)
			if parent == dir {
				break
			}
			dir = parent
		}
	}
	return ""
}

func isRepoRoot(dir string) bool {
	info, err := os.Stat(filepath.Join(dir, "AGENTS.md"))
	if err != nil || info.IsDir() {
		return false
	}
	info, err = os.Stat(filepath.Join(dir, "SBE-Design"))
	return err == nil && info.IsDir()
}

func cwdOrEmpty() string {
	dir, err := os.Getwd()
	if err != nil {
		return ""
	}
	return dir
}

func executableDirOrEmpty() string {
	exe, err := os.Executable()
	if err != nil {
		return ""
	}
	return filepath.Dir(exe)
}
