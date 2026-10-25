// Package ws 实现服务端所需的最小 RFC 6455 WebSocket：
// HTTP Upgrade 握手、帧编解码（含分片、掩码、ping/pong/close）与消息读写。
// 仅依赖标准库，避免外部模块依赖；客户端方向（掩码发送）供集成测试使用。
package ws

import (
	"encoding/binary"
	"errors"
	"io"
)

// 操作码。
const (
	opContinuation = 0x0
	OpText         = 0x1
	OpBinary       = 0x2
	OpClose        = 0x8
	OpPing         = 0x9
	OpPong         = 0xA
)

const (
	finalBit  = 0x80
	maskBit   = 0x80
	maxHeader = 14 // 2 + 8 + 4
	// maxMessageSize 单条消息上限（1 MiB），超出视为协议错误，防御异常客户端。
	maxMessageSize = 1 << 20
)

// ErrClosed 连接已关闭。
var ErrClosed = errors.New("ws: connection closed")

type frameHeader struct {
	fin     bool
	opcode  byte
	masked  bool
	length  int64
	maskKey [4]byte
}

func readFrameHeader(r io.Reader) (frameHeader, error) {
	var buf [2]byte
	if _, err := io.ReadFull(r, buf[:]); err != nil {
		return frameHeader{}, err
	}
	h := frameHeader{
		fin:    buf[0]&finalBit != 0,
		opcode: buf[0] & 0x0F,
		masked: buf[1]&maskBit != 0,
	}
	length := int64(buf[1] & 0x7F)
	switch length {
	case 126:
		var ext [2]byte
		if _, err := io.ReadFull(r, ext[:]); err != nil {
			return frameHeader{}, err
		}
		length = int64(binary.BigEndian.Uint16(ext[:]))
	case 127:
		var ext [8]byte
		if _, err := io.ReadFull(r, ext[:]); err != nil {
			return frameHeader{}, err
		}
		length = int64(binary.BigEndian.Uint64(ext[:]))
	}
	h.length = length
	if h.masked {
		if _, err := io.ReadFull(r, h.maskKey[:]); err != nil {
			return frameHeader{}, err
		}
	}
	return h, nil
}

func applyMask(payload []byte, key [4]byte) {
	for i := range payload {
		payload[i] ^= key[i&3]
	}
}

// writeFrame 写一帧。mask=true 时按客户端方向掩码（随机密钥由调用方提供）。
func writeFrame(w io.Writer, opcode byte, payload []byte, mask bool, maskKey [4]byte) error {
	header := make([]byte, 0, maxHeader)
	first := finalBit | opcode
	header = append(header, first)

	lenByte := byte(0)
	if mask {
		lenByte |= maskBit
	}
	switch {
	case len(payload) < 126:
		header = append(header, lenByte|byte(len(payload)))
	case len(payload) <= 0xFFFF:
		header = append(header, lenByte|126)
		var ext [2]byte
		binary.BigEndian.PutUint16(ext[:], uint16(len(payload)))
		header = append(header, ext[:]...)
	default:
		header = append(header, lenByte|127)
		var ext [8]byte
		binary.BigEndian.PutUint64(ext[:], uint64(len(payload)))
		header = append(header, ext[:]...)
	}

	body := payload
	if mask {
		header = append(header, maskKey[:]...)
		body = make([]byte, len(payload))
		copy(body, payload)
		applyMask(body, maskKey)
	}

	if _, err := w.Write(header); err != nil {
		return err
	}
	if len(body) == 0 {
		return nil
	}
	_, err := w.Write(body)
	return err
}
