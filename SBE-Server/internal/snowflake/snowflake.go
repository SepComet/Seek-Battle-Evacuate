// Package snowflake 雪花算法 uid 铸造：41 位毫秒时间戳 | 10 位 worker | 12 位序列。
// 41 位时间戳自纪元起可用约 69 年；多实例部署时 workerID 必须互异。
package snowflake

import (
	"errors"
	"fmt"
	"sync"
	"time"
)

// Epoch 自定义纪元：2026-01-01T00:00:00Z（Unix 毫秒）。
const Epoch int64 = 1767225600000

const (
	workerBits  = 10
	seqBits     = 12
	maxWorkerID = int64(1)<<workerBits - 1 // 1023
	maxSeq      = int64(1)<<seqBits - 1    // 4095
	workerShift = seqBits
	timeShift   = seqBits + workerBits
)

// ErrClockRolledBack 系统时钟回拨，拒绝铸号（防止铸出重复 uid）。
var ErrClockRolledBack = errors.New("snowflake: clock moved backwards")

// Node 单实例铸号器。线程安全。
type Node struct {
	mu       sync.Mutex
	workerID int64
	lastMs   int64
	seq      int64
	now      func() int64 // 可注入时钟（测试）
}

// New 创建铸号器。workerID 取值 0~1023，多实例部署必须互异。
func New(workerID int64) (*Node, error) {
	return NewWithClock(workerID, func() int64 { return time.Now().UnixMilli() })
}

// NewWithClock 同 New，允许注入毫秒时钟（测试用）。
func NewWithClock(workerID int64, now func() int64) (*Node, error) {
	if workerID < 0 || workerID > maxWorkerID {
		return nil, fmt.Errorf("snowflake: worker id %d out of range [0, %d]", workerID, maxWorkerID)
	}
	return &Node{workerID: workerID, now: now}, nil
}

// Next 铸造下一个全局唯一 uid。
func (n *Node) Next() (int64, error) {
	n.mu.Lock()
	defer n.mu.Unlock()

	ms := n.now()
	if ms < n.lastMs {
		return 0, fmt.Errorf("%w: current %d < last %d", ErrClockRolledBack, ms, n.lastMs)
	}
	if ms < Epoch {
		return 0, fmt.Errorf("snowflake: clock %d before epoch %d", ms, Epoch)
	}
	if ms == n.lastMs {
		n.seq = (n.seq + 1) & maxSeq
		if n.seq == 0 {
			// 本毫秒序列耗尽，等待时钟前进到下一毫秒。
			for ms <= n.lastMs {
				ms = n.now()
			}
		}
	} else {
		n.seq = 0
	}
	n.lastMs = ms

	return (ms - Epoch) << timeShift | n.workerID << workerShift | n.seq, nil
}
