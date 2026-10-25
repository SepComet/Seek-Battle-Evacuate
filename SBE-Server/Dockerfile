# sbe-server 镜像：多阶段构建，产物为静态二进制 + Luban 导出配置。
# 构建上下文为 SBE-Server/ 目录：docker build -t sbe-server .

FROM golang:1.27-alpine AS build
WORKDIR /src
# 本机代理不稳时可用 --build-arg GOPROXY 覆盖
ARG GOPROXY=https://goproxy.cn,direct
ENV GOPROXY=${GOPROXY}
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" -o /out/sbe-server ./cmd/sbe-server

FROM alpine:3.21
WORKDIR /app
COPY --from=build /out/sbe-server /app/sbe-server
COPY config /app/config
EXPOSE 7777
ENTRYPOINT ["/app/sbe-server"]
CMD ["-store", "memory", "-addr", "0.0.0.0:7777"]
