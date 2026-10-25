package snowflake

import (
	"errors"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

func newTestNode(t *testing.T, workerID int64, now func() int64) *Node {
	t.Helper()
	n, err := NewWithClock(workerID, now)
	if err != nil {
		t.Fatalf("new node: %v", err)
	}
	return n
}

func TestNewRejectsWorkerOutOfRange(t *testing.T) {
	for _, id := range []int64{-1, maxWorkerID + 1} {
		if _, err := New(id); err == nil {
			t.Fatalf("worker id %d must be rejected", id)
		}
	}
	if _, err := New(maxWorkerID); err != nil {
		t.Fatalf("worker id %d must be accepted: %v", maxWorkerID, err)
	}
}

// 时钟随铸造次数前进：每 maxSeq+1 次调用推进一个虚拟毫秒，
// 并发下既覆盖同毫秒的序列位、也覆盖跨毫秒推进。
func TestNextUniqueUnderConcurrency(t *testing.T) {
	var calls atomic.Int64
	n := newTestNode(t, 1, func() int64 {
		return Epoch + calls.Add(1)/(maxSeq+1)
	})

	const workers, perWorker = 8, 5000
	var mu sync.Mutex
	seen := make(map[int64]bool, workers*perWorker)
	var wg sync.WaitGroup
	for i := 0; i < workers; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			local := make([]int64, 0, perWorker)
			for j := 0; j < perWorker; j++ {
				id, err := n.Next()
				if err != nil {
					t.Errorf("next: %v", err)
					return
				}
				local = append(local, id)
			}
			mu.Lock()
			defer mu.Unlock()
			for _, id := range local {
				if seen[id] {
					t.Errorf("duplicate uid %d", id)
					return
				}
				seen[id] = true
			}
		}()
	}
	wg.Wait()
	if len(seen) != workers*perWorker {
		t.Fatalf("minted %d unique ids, want %d", len(seen), workers*perWorker)
	}
}

func TestNextMonotonicAndEncodedFields(t *testing.T) {
	n := newTestNode(t, 7, func() int64 { return Epoch + 1000 })

	prev := int64(0)
	for i := 0; i < 10; i++ {
		id, err := n.Next()
		if err != nil {
			t.Fatalf("next: %v", err)
		}
		if id <= prev {
			t.Fatalf("uid %d not greater than previous %d", id, prev)
		}
		prev = id
		if got := id >> workerShift & maxWorkerID; got != 7 {
			t.Fatalf("worker bits %d, want 7", got)
		}
		if got := id & maxSeq; got != int64(i) {
			t.Fatalf("sequence bits %d, want %d", got, i)
		}
		if got := (id >> timeShift) + Epoch; got != Epoch+1000 {
			t.Fatalf("timestamp bits %d, want %d", got, Epoch+1000)
		}
	}
}

// 铸满一个毫秒的序列后，下一发应自旋等待时钟前进，并在新毫秒从序列 0 重新开始。
func TestNextWaitsForClockWhenSequenceExhausted(t *testing.T) {
	var ticks atomic.Int64
	n := newTestNode(t, 1, func() int64 { return Epoch + ticks.Load() })

	for i := 0; i < int(maxSeq)+1; i++ {
		if _, err := n.Next(); err != nil {
			t.Fatalf("next %d: %v", i, err)
		}
	}

	go func() {
		time.Sleep(20 * time.Millisecond)
		ticks.Store(1)
	}()
	id, err := n.Next()
	if err != nil {
		t.Fatalf("next after exhaustion: %v", err)
	}
	if got := id & maxSeq; got != 0 {
		t.Fatalf("new millisecond must restart sequence at 0, got %d", got)
	}
	if got := id >> timeShift; got != 1 {
		t.Fatalf("timestamp bits %d, want 1", got)
	}
}

func TestNextRejectsClockRollback(t *testing.T) {
	var ms atomic.Int64
	ms.Store(Epoch + 100)
	n := newTestNode(t, 1, func() int64 { return ms.Load() })
	if _, err := n.Next(); err != nil {
		t.Fatalf("next: %v", err)
	}

	ms.Store(Epoch + 50)
	if _, err := n.Next(); !errors.Is(err, ErrClockRolledBack) {
		t.Fatalf("want ErrClockRolledBack, got %v", err)
	}

	ms.Store(Epoch + 101)
	if _, err := n.Next(); err != nil {
		t.Fatalf("recovered after clock advanced again: %v", err)
	}
}
