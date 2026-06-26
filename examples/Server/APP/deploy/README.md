# 外网部署（Linux + WebSocket + MongoDB 同机）

本目录用于把示例服务端发布到外网 Linux 服务器。

## 文件

| 文件 | 作用 |
|---|---|
| `Fantasy.config.prod` | 外网版配置模板：`outerIP` 占位符 + 只保留 WebSocket Gate（KCP/HTTP 已注释） |
| `deploy.sh` | 本机执行：`dotnet publish` + 写入公网 IP + `scp` 上传 |
| `fantasy.service` | 服务器上的 systemd 守护进程单元 |

## 用 .pem 私钥连服务器（云厂商常见）

如果云厂商给了 `.pem` 私钥，直接用 `SSH_KEY` 指向它即可，无需下面的 ssh-keygen 流程：

```bash
# Git Bash 里 Windows 路径写成 /c/... 形式
export SSH_KEY="/c/Users/pc/Desktop/蛙蛙.pem"

# 私钥权限必须收紧，否则 ssh 会拒绝使用
chmod 600 "$SSH_KEY"

# 先验证能连上（user 视云厂商而定：阿里云 root、AWS Ubuntu 镜像多为 ubuntu/ec2-user）
ssh -i "$SSH_KEY" root@你的公网IP 'echo OK'

# 之后部署带上 SSH_KEY 即可
PUBLIC_IP=你的公网IP SERVER_USER=root SSH_KEY="$SSH_KEY" ./deploy.sh
```

> 安全：`.pem` 私钥不要提交进仓库、不要贴进聊天或截图。若已外泄，去云控制台轮换密钥对。

## 配置免密 SSH（可选，无 .pem 时用）

`deploy.sh` 用到 `ssh`/`scp`，配好密钥后免输密码、也更安全。

```bash
# 1. 本机生成密钥（已有 ~/.ssh/id_ed25519 可跳过，一路回车即可）
ls ~/.ssh/id_ed25519.pub 2>/dev/null || ssh-keygen -t ed25519 -C "fantasy-deploy"

# 2. 把公钥装到服务器（Git Bash 通常没有 ssh-copy-id，用下面这条通用写法）
cat ~/.ssh/id_ed25519.pub | ssh root@你的公网IP \
  "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"

# 3. 验证：应直接进去、不再要密码
ssh root@你的公网IP 'echo OK'
```

可选：在 `~/.ssh/config` 里加别名，之后 `deploy.sh` 用 `SERVER_HOST=fantasy-prod` 即可：

```
Host fantasy-prod
    HostName 你的公网IP
    User root
    IdentityFile ~/.ssh/id_ed25519
```

> 服务器若禁用了密码登录、或只允许密钥，第 2 步需先用控制台/已有方式登入再贴公钥。
> 生产环境建议用非 root 部署账号，并把 `SERVER_USER` 指向它。

## 一次性：服务器准备 MongoDB（同机，只绑回环）

```bash
sudo docker run -d --name mongo --restart=always \
  -p 127.0.0.1:27017:27017 \
  -v /opt/fantasy/mongodata:/data/db \
  mongo:7
```

## 一次性：放行端口

WebSocket 是 TCP，只需开 20001。**云控制台安全组也要放行 20001/TCP**（这层最容易漏）。

```bash
sudo ufw allow 20001/tcp
```

`27017` 与所有 `innerPort`（11001~11007）不要对公网开放。

## 一次性：安装 systemd 单元（先上传过一次代码后）

```bash
sudo cp /opt/fantasy/server/deploy/fantasy.service /etc/systemd/system/fantasy.service  # 或手动 scp
sudo systemctl daemon-reload
sudo systemctl enable --now fantasy
```

## 每次发布（本机 Git Bash 执行）

```bash
PUBLIC_IP=你的公网IP SERVER_USER=root ./deploy.sh
ssh root@你的公网IP 'sudo systemctl restart fantasy && sudo journalctl -u fantasy -f'
```

只想本地打包不上传：`PUBLIC_IP=你的公网IP ./deploy.sh --no-upload`，产物在 `deploy/out/server`。

## 已知坑

- **WebSocket 的 `outerBindIP` 必须填 `+`，不能填 `0.0.0.0`**：WebSocket 走 .NET `HttpListener`，
  Linux 下 `http://0.0.0.0:port/` 前缀会抛 `HttpListenerException(50) The request is not supported`，
  端口起不来。`Fantasy.config.prod` 已用 `+`。（TCP/KCP 反过来要用 `0.0.0.0`。）
- **Release 模式必须带 `--pid`**：`Main --m Release` 只会启动 `--pid` 指定 ID 的进程，缺省 0 会
  报 `not found processConfig by Id:0`。`fantasy.service` 已带 `--pid 1`（对应 `<process id="1">`）。
- **不要 `ufw enable`**：默认 `deny incoming` 会连 22 端口 SSH 一起挡掉。主机层放开、靠云安全组控流即可。
- **云安全组要单独放行 20001/TCP**：主机能监听 ≠ 外网能连，阿里云/腾讯云控制台的安全组是另一道闸门。

## 验证

1. `systemctl status fantasy` → active
2. 服务器 `ss -ltnp | grep 20001` → 监听 `0.0.0.0:20001`
3. 外部 `telnet 公网IP 20001` → 通
4. 客户端连 `ws://公网IP:20001` 登录走通
5. `docker exec -it mongo mongosh` → `show dbs` 能看到 `fantasy_main1`

## H5/WebGL + HTTPS 客户端

浏览器在 https 页面下禁止 `ws://`，需用 Nginx 反代成 `wss://域名/ws` → `127.0.0.1:20001`，
此时对外只开 443，20001 仅本地。配置见对话记录中的 Nginx 片段。
