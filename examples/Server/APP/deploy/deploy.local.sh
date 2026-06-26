#!/usr/bin/env bash
# ============================================================================
# 本机一键发布的固定参数封装（被 deploy.bat 调用，也可直接 bash deploy.local.sh 跑）
# 换公网 IP / 私钥路径，改这里即可。
# 传参会透传给 deploy.sh，例如：  bash deploy.local.sh --no-upload
# ============================================================================
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

export PUBLIC_IP="121.199.24.31"
export SSH_KEY="/d/work/TEngine_block/Fantasy/蛙蛙.pem"

# 收紧私钥权限，避免 ssh 报 UNPROTECTED PRIVATE KEY FILE（Windows 上不生效也不影响）
chmod 600 "$SSH_KEY" 2>/dev/null || true

./deploy.sh "$@"
