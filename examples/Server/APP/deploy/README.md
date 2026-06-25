# 外网部署（Linux + WebSocket + MongoDB 同机）

本目录用于把示例服务端发布到外网 Linux 服务器。

## 文件

| 文件 | 作用 |
|---|---|
| `Fantasy.config.prod` | 外网版配置模板：`outerIP` 占位符 + 只保留 WebSocket Gate（KCP/HTTP 已注释） |
| `deploy.sh` | 本机执行：`dotnet publish` + 写入公网 IP + `scp` 上传 |
| `fantasy.service` | 服务器上的 systemd 守护进程单元 |

## 一次性：配置免密 SSH（本机 Git Bash 执行）

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

## 验证

1. `systemctl status fantasy` → active
2. 服务器 `ss -ltnp | grep 20001` → 监听 `0.0.0.0:20001`
3. 外部 `telnet 公网IP 20001` → 通
4. 客户端连 `ws://公网IP:20001` 登录走通
5. `docker exec -it mongo mongosh` → `show dbs` 能看到 `fantasy_main1`

## H5/WebGL + HTTPS 客户端

浏览器在 https 页面下禁止 `ws://`，需用 Nginx 反代成 `wss://域名/ws` → `127.0.0.1:20001`，
此时对外只开 443，20001 仅本地。配置见对话记录中的 Nginx 片段。
