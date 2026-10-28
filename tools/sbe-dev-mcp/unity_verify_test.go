package main

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

// runUnityCapture 的「结果即收工」路径：
// doneFile 出现后宽限窗口内进程自然退出 → 不算提前收工；
// 宽限窗口结束仍未退出 → Kill 且 earlyReap=true。
func TestRunUnityCaptureEarlyReap(t *testing.T) {
	dir := t.TempDir()
	doneFile := filepath.Join(dir, "results.xml")

	// 用例 1：进程存活远超宽限窗口，doneFile 出现 → Kill 收工。
	// ping -n 60 约跑 60 秒，远长于 1 秒宽限 + 短超时。
	go func() {
		time.Sleep(1500 * time.Millisecond) // 先让进程跑起来，再落结果文件
		_ = os.WriteFile(doneFile, []byte("<dummy/>"), 0o644)
	}()
	start := time.Now()
	_, exitCode, runErr, earlyReap := runUnityCapture("ping", []string{"-n", "60", "127.0.0.1"}, 60, doneFile, 1)
	elapsed := time.Since(start)
	if runErr != nil {
		t.Fatalf("case1 runErr = %v", runErr)
	}
	if !earlyReap {
		t.Fatalf("case1 earlyReap = false, want true（进程存活超宽限窗口，应被 Kill）")
	}
	if elapsed > 10*time.Second {
		t.Fatalf("case1 elapsed = %s，收工应在数秒内完成", elapsed)
	}
	_ = exitCode // 被 Kill 的进程退出码不代表结论，此处不作断言

	// 用例 2：doneFile 出现前进程已自然退出 → earlyReap=false，正常返回。
	_ = os.Remove(doneFile)
	_, _, runErr2, earlyReap2 := runUnityCapture("ping", []string{"-n", "1", "127.0.0.1"}, 60, doneFile, 1)
	if runErr2 != nil {
		t.Fatalf("case2 runErr = %v", runErr2)
	}
	if earlyReap2 {
		t.Fatalf("case2 earlyReap = true, want false（进程自然退出）")
	}
}
