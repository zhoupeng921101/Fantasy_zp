#!/usr/bin/env bash
# ============================================================================
# Fantasy 服务端发布 + 上传脚本（Linux + WebSocket 目标）
#
# 用法（在本机 Git Bash / WSL 执行）：
#   PUBLIC_IP=1.2.3.4 SERVER_USER=root ./deploy.sh            # 发布并 scp 上传
#   PUBLIC_IP=1.2.3.4 ./deploy.sh --no-upload                 # 只发布到本地 out/，不上传
#
# 环境变量：
#   PUBLIC_IP   必填，服务器公网 IP（写进 Fantasy.config 的 outerIP）
#   SERVER_USER SSH 用户名，默认 root
#   SERVER_HOST SSH 主机，默认等于 PUBLIC_IP
#   REMOTE_DIR  远端目录，默认 /opt/fantasy/server
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$SCRIPT_DIR/../Main/Main.csproj"
PROD_CONFIG="$SCRIPT_DIR/Fantasy.config.prod"
OUT="$SCRIPT_DIR/out/server"

TFM="net8.0"
RID="linux-x64"

PUBLIC_IP="${PUBLIC_IP:-}"
SERVER_USER="${SERVER_USER:-root}"
SERVER_HOST="${SERVER_HOST:-$PUBLIC_IP}"
REMOTE_DIR="${REMOTE_DIR:-/opt/fantasy/server}"

UPLOAD=1
[[ "${1:-}" == "--no-upload" ]] && UPLOAD=0

if [[ -z "$PUBLIC_IP" ]]; then
  echo "ERROR: 必须设置 PUBLIC_IP，例如  PUBLIC_IP=1.2.3.4 ./deploy.sh" >&2
  exit 1
fi

echo "==> 清理旧产物 $OUT"
rm -rf "$OUT"

echo "==> dotnet publish ($TFM / $RID / self-contained)"
dotnet publish "$PROJ" -c Release -f "$TFM" -r "$RID" --self-contained true -o "$OUT"

echo "==> 写入外网版 Fantasy.config（outerIP=$PUBLIC_IP）"
sed "s/__PUBLIC_IP__/$PUBLIC_IP/g" "$PROD_CONFIG" > "$OUT/Fantasy.config"

echo "==> 本地产物就绪：$OUT"

if [[ "$UPLOAD" -eq 0 ]]; then
  echo "==> --no-upload 指定，跳过上传。"
  exit 0
fi

echo "==> 上传到 $SERVER_USER@$SERVER_HOST:$REMOTE_DIR"
ssh "$SERVER_USER@$SERVER_HOST" "mkdir -p '$REMOTE_DIR'"
scp -r "$OUT/." "$SERVER_USER@$SERVER_HOST:$REMOTE_DIR/"
ssh "$SERVER_USER@$SERVER_HOST" "chmod +x '$REMOTE_DIR/Main'"

echo "==> 完成。重启服务："
echo "    ssh $SERVER_USER@$SERVER_HOST 'sudo systemctl restart fantasy && sudo journalctl -u fantasy -f'"
