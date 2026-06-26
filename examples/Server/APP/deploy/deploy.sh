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
#   SSH_KEY     可选，私钥文件路径（.pem）；设置后 ssh/scp 自动带 -i
#               例：SSH_KEY="/c/Users/pc/Desktop/蛙蛙.pem"
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
SSH_KEY="${SSH_KEY:-}"

# 有 SSH_KEY 则带 -i；同时关掉首次连接的指纹确认交互
SSH_OPTS=()
if [[ -n "$SSH_KEY" ]]; then
  if [[ ! -f "$SSH_KEY" ]]; then
    echo "ERROR: SSH_KEY 指向的文件不存在：$SSH_KEY" >&2
    exit 1
  fi
  SSH_OPTS=(-i "$SSH_KEY" -o StrictHostKeyChecking=accept-new)
fi

UPLOAD=1
[[ "${1:-}" == "--no-upload" ]] && UPLOAD=0

if [[ -z "$PUBLIC_IP" ]]; then
  echo "ERROR: 必须设置 PUBLIC_IP，例如  PUBLIC_IP=1.2.3.4 ./deploy.sh" >&2
  exit 1
fi

echo "==> 清理旧产物 $OUT"
rm -rf "$OUT"

echo "==> dotnet publish ($TFM / $RID / self-contained)"
# ErrorOnDuplicatePublishOutputFiles=false: Entity 与 Fantasy.Net 包各带一份 Fantasy.config，
# 发布到同一相对路径会冲突(NETSDK1152)；忽略即可——下面会用 Fantasy.config.prod 覆盖最终产物。
dotnet publish "$PROJ" -c Release -f "$TFM" -r "$RID" --self-contained true \
  -p:ErrorOnDuplicatePublishOutputFiles=false -o "$OUT"

echo "==> 写入外网版 Fantasy.config（outerIP=$PUBLIC_IP）"
sed "s/__PUBLIC_IP__/$PUBLIC_IP/g" "$PROD_CONFIG" > "$OUT/Fantasy.config"

echo "==> 本地产物就绪：$OUT"

if [[ "$UPLOAD" -eq 0 ]]; then
  echo "==> --no-upload 指定，跳过上传。"
  exit 0
fi

# 先停服务：正在运行的 Main 无法被 scp 覆盖（Linux 报 Text file busy）
echo "==> 停止 fantasy 服务（若已安装）"
ssh "${SSH_OPTS[@]}" "$SERVER_USER@$SERVER_HOST" "systemctl stop fantasy 2>/dev/null || true"

echo "==> 增量上传到 $SERVER_USER@$SERVER_HOST:$REMOTE_DIR（md5 比对，只传变化/新增文件）"
ssh "${SSH_OPTS[@]}" "$SERVER_USER@$SERVER_HOST" "mkdir -p '$REMOTE_DIR'"

# 归一化：消除 Git Bash(二进制模式 'hash *path') 与 Linux(文本模式 'hash  path') 的格式差异，
# 统一成 "hash path"（hash=前32列，path=第35列起）再排序，否则同内容文件会被误判为“变化”。
norm() { awk 'NF{print substr($0,1,32)" "substr($0,35)}' | LC_ALL=C sort; }
# 远端清单（目录为空时输出空）
remote_md5="$(ssh "${SSH_OPTS[@]}" "$SERVER_USER@$SERVER_HOST" \
  "cd '$REMOTE_DIR' && find . -type f -exec md5sum {} + 2>/dev/null" | norm || true)"
# 本地清单
local_md5="$(cd "$OUT" && find . -type f -exec md5sum {} + | norm)"
# comm -23 取“只在本地出现”的行（新增 或 内容变更）→ 取路径（第一个空格之后）
changed="$(LC_ALL=C comm -23 <(printf '%s\n' "$local_md5") <(printf '%s\n' "$remote_md5") | cut -d' ' -f2-)"
changed="$(printf '%s\n' "$changed" | grep -v '^$' || true)"
# 注：本方案只增量上传，不删除远端已删本地的文件（避免误删服务器上的 Logs/GameConfigBytes 等）

if [ -z "$changed" ]; then
  echo "==> 无文件变化，跳过传输。"
else
  n="$(printf '%s\n' "$changed" | wc -l | tr -d ' ')"
  echo "==> 变化文件 $n 个（共 $(printf '%s\n' "$local_md5" | wc -l | tr -d ' ') 个）："
  printf '%s\n' "$changed" | sed 's#^\./#    #'
  # 打包变化文件 → 流式传输 → 远端解包（一次 ssh 连接，保留相对目录结构）
  printf '%s\n' "$changed" | tar -C "$OUT" -czf - -T - \
    | ssh "${SSH_OPTS[@]}" "$SERVER_USER@$SERVER_HOST" "tar -C '$REMOTE_DIR' -xzf -"
fi
ssh "${SSH_OPTS[@]}" "$SERVER_USER@$SERVER_HOST" "chmod +x '$REMOTE_DIR/Main'"

echo "==> 启动 fantasy 服务"
if ssh "${SSH_OPTS[@]}" "$SERVER_USER@$SERVER_HOST" "systemctl start fantasy" 2>/dev/null; then
  echo "==> 启动完成，当前状态与最近日志："
  ssh "${SSH_OPTS[@]}" "$SERVER_USER@$SERVER_HOST" \
    "systemctl is-active fantasy; journalctl -u fantasy -n 15 --no-pager"
else
  echo "!! 启动失败：fantasy.service 可能尚未安装（首次部署常见）。"
  echo "   请在服务器上装一次 systemd 单元（见 deploy/README.md「安装 systemd 单元」一节），之后本脚本即可自动启停。"
fi
