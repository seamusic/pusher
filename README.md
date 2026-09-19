# 消息推送服务

_✨ Go 版 [message-pusher](https://github.com/songquanpeng/message-pusher) 的 .NET 重写：API / 前端 / 数据三层兼容，可灰度切换 ✨_

[部署](#部署) · [配置](#配置) · [用法](#用法) · [开发](#开发) · [设计文档](./docs/dotnet-design.md)

## 描述

自托管的统一消息推送网关：对外一个极简 API（`/push/<username>`），按用户配置的通道转发到邮件、微信、飞书、钉钉、Telegram 等 **16 种渠道**；同时提供 Web 控制台管理用户、通道、消息、Webhook 与系统配置。

1. **多种消息推送方式**：
   + 邮件、微信测试号、企业微信应用号 / 群机器人
   + 飞书自建应用 / 群机器人、钉钉群机器人
   + Bark、Telegram、Discord、腾讯云自定义告警
   + WebSocket 客户端、OneBot（QQ）
   + **群组消息**：多个通道组合成群组，一次推送到多个渠道
   + **自定义消息**：自定义请求 URL 与请求体，对接第三方服务
   + `none`：仅落库不发送
2. 支持**自定义 Webhook**，反向适配已有系统，无需改其代码。
3. Web 端可编辑、管理已发消息，新消息通过 **SSE** 即时刷新。
4. 支持**异步**发送，可用 UUID 查询状态。
5. 用户管理与多种登录：邮箱、GitHub OAuth、微信公众号授权（需额外部署 WeChat Server）。
6. 支持 Markdown（Markdig，GFM + 脚注）。
7. 支持 Cloudflare Turnstile 校验。
8. 支持在线发布公告、关于页与页脚。
9. API **兼容** [Server 酱](https://sct.ftqq.com/) 等字段（`short` / `desp` / `openid`）。

前端为原版 React SPA **零改动**复用，构建产物放在 `MessagePusher.Api/wwwroot`。

## 兼容性契约

| 契约 | 说明 |
|---|---|
| 响应体 | `{ "success": bool, "message": string, "data": ... }` |
| 字段命名 | snake_case |
| HTTP 语义 | 业务失败 **200 + success:false**；未登录 401；限流 429 |
| ID | `link` / `token` 为 32 位无横线 UUID |
| 密码 | BCrypt，与 Go `$2a$` / `$2b$`（cost=10）互认，老用户可直接登录 |
| 数据表 | 5 张表（users / channels / messages / options / webhooks），列名与现网一致 |

默认账号：`root` / `123456`（首次启动自动创建）。

## 技术栈

- **.NET 10** + ASP.NET Core（Controllers）
- **EF Core**：SQLite 默认 / MySQL（Pomelo）
- Cookie 认证 + 数据保护密钥持久化
- 可选 Redis（限流 + 数据保护密钥）
- Serilog、MailKit、Markdig、Swashbuckle（仅 Development）

详见 [dotnet-design.md](./docs/dotnet-design.md) §2。

## 项目结构

```
dotnet/
├─ src/MessagePusher.Domain         # 实体、枚举、常量、领域异常
├─ src/MessagePusher.Application    # 业务服务、16 通道、SSE / WebSocket、异步队列
├─ src/MessagePusher.Infrastructure # EF Core、仓储、邮件、Markdown、限流、选项
├─ src/MessagePusher.Api            # HTTP 管道、Controllers、wwwroot（SPA）
├─ tests/                           # 单元 / 集成测试
└─ docs/                            # 设计与迁移文档
```

分层：`Api → Application → Domain`，仅 Infrastructure 依赖外部库。

## 部署

### Docker Compose（推荐）

在 `dotnet/` 目录：

```bash
export SESSION_SECRET=please-change-me
docker compose up -d --build
```

构建上下文为仓库根目录（需能读到 `message-pusher/web` 以编译前端）。端口 **3000**，数据卷 `./data`（SQLite）。

更新后重新 `docker compose up -d --build`。

### Docker

在**仓库根目录**：

```bash
docker build -f dotnet/Dockerfile -t message-pusher-dotnet .
docker run -d --restart always --name message-pusher \
  -p 3000:3000 -e TZ=Asia/Shanghai \
  -e AppSettings__SessionSecret=please-change-me \
  -v /home/ubuntu/data/message-pusher:/data \
  message-pusher-dotnet
```

数据目录需可写。其后用 Nginx 反代即可。

Nginx 参考：

```
server {
   server_name push.example.com;

   location / {
          client_max_body_size 64m;
          proxy_http_version 1.1;
          proxy_pass http://localhost:3000;
          proxy_set_header Host $host;
          proxy_set_header X-Forwarded-For $remote_addr;
          proxy_set_header X-Forwarded-Proto $scheme;
          proxy_set_header Upgrade $http_upgrade;
          proxy_set_header Connection "upgrade";
          proxy_cache_bypass $http_upgrade;
          proxy_read_timeout 300s;
          proxy_send_timeout 300s;
   }
}
```

使用 WebSocket 客户端时，`proxy_read_timeout` / `proxy_send_timeout` 必须大于 1 分钟。

### 从源码运行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download) 与 Node.js（构建前端）。

```bash
# 1. 构建前端并拷入 wwwroot
cd message-pusher/web
yarn install
yarn build
cp -r build/* ../../dotnet/src/MessagePusher.Api/wwwroot/

# 2. 运行后端
cd ../../dotnet
dotnet run --project src/MessagePusher.Api -- --port 3000 --log-dir ./logs
```

访问 http://localhost:3000/ ，使用 `root` / `123456` 登录。生产环境请立刻改密，并设置 `SESSION_SECRET`。

## 配置

可通过 **appsettings**、**环境变量**或**命令行**配置。关键项缺失时进程 fail-fast 退出。

等到系统启动后，用 `root` 登录做 SMTP、通道、登录注册等后台配置。

### 环境变量

兼容原版 Go 变量名，同时支持 ASP.NET 分层键（`__` 分隔）：

| 变量 | 作用 |
|---|---|
| `SESSION_SECRET` / `AppSettings__SessionSecret` | **必填**。固定会话密钥，重启后 Cookie 仍有效 |
| `REDIS_CONN_STRING` / `ConnectionStrings__Redis` | 设置后用 Redis 做限流与数据保护密钥存储 |
| `SQL_DSN` / `ConnectionStrings__Default` | 设置后使用 MySQL（并自动将 `Database:Provider` 设为 `mysql`） |
| `SQLITE_PATH` / `Database__SqlitePath` | SQLite 文件路径，默认 `message-pusher.db` |
| `Database__Provider` | `sqlite`（默认）或 `mysql` |
| `PORT` | 监听端口，默认 `3000` |
| `CHANNEL_URL_ALLOW_NON_HTTPS` | 自定义通道是否允许非 HTTPS URL |
| `AppSettings__LogDir` | 日志目录（也可用 `--log-dir`） |
| `AppSettings__DataProtectionKeysPath` | 数据保护密钥目录，默认 `keys` |

Docker 示例：`docker run -e SESSION_SECRET=random_string ...`

### 命令行

```
--port           监听端口，默认 3000
--log-dir        日志目录；不设则只打控制台
--version        打印版本并退出
--help           打印帮助并退出
```

### 进一步配置

1. 系统设置：服务器地址、是否允许注册、SMTP、Turnstile / OAuth 等。
2. 个人设置：改用户名密码、绑定邮箱（启用邮件通道）。
3. 推送设置：默认通道、推送 token、按页面提示配置各通道并点「测试」。
4. 公告 / 关于 / 页脚：对外提供服务时可自定义。

## 用法

推送 API 与原版一致。

1. URL：`https://<domain>/push/<username>`
2. `GET`：`?title=<标题>&description=<描述>&content=<Markdown>&channel=<通道名>&token=<token>`
   1. `title`：选填。
   2. `description`：必填，可写 `desp`（Server 酱兼容）。
   3. `content`：选填 Markdown。
   4. `channel`：选填，不填则用后台默认通道。填的是通道**名称**，类型包括：
      `email` / `test` / `corp_app` / `lark_app` / `corp` / `lark` / `ding` / `bark` / `client` / `telegram` / `discord` / `one_bot` / `group` / `custom` / `tencent_alarm` / `none`
   5. `token`：若后台设置了推送 token 则必填；也可放 HTTP `Authorization`。全局 token 可鉴权任何通道，通道 token 只能鉴权对应通道。
   6. `url`：选填；不填则生成消息详情页 URL。
   7. `to`：选填。`@all` 或 `user1|user2`。
   8. `async=true`：异步发送，返回 `uuid`，可用 `GET /api/message/status/{uuid}` 查状态。
   9. `render_mode`：`markdown`（默认）/ `code` / `raw`。
3. `POST`：字段同上。JSON 时 `Content-Type` 必须为 `application/json`，否则按 Form 处理。`token` 也可放在 Query。

**通道字段支持：**

| 通道 | `title` | `description` | `content` | `url` | `to` | Markdown |
|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| `email` | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ |
| `test` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `corp_app` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `corp` | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `lark` / `lark_app` | ❌ | ✅ | ✅ | ❌ | ✅ | ✅ |
| `ding` | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `bark` | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| `client` | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| `telegram` | ❌ | ❌ | ✅ | ❌ | ✅ | ✅ |
| `discord` | ❌ | ❌ | ✅ | ❌ | ✅ | ❌ |
| `tencent_alarm` | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ |

多数通道 `description` 与 `content` 不要同时填：纯文本用 `description`，Markdown 用 `content`。

**Bash：**

```bash
MESSAGE_PUSHER_SERVER="http://localhost:3000"
MESSAGE_PUSHER_USERNAME="root"
MESSAGE_PUSHER_TOKEN="666"

curl -s -X POST "$MESSAGE_PUSHER_SERVER/push/$MESSAGE_PUSHER_USERNAME" \
  -H 'Content-Type: application/json' \
  -d '{"title":"标题","description":"描述","content":"**Markdown**","token":"'"$MESSAGE_PUSHER_TOKEN"'"}'
```

**Python：**

```python
import requests

res = requests.post("http://localhost:3000/push/root", json={
    "title": "标题",
    "description": "描述",
    "content": "**Markdown 内容**",
    "token": "666",
}).json()
if not res["success"]:
    print(res["message"])
```

更多语言示例见原版 [message-pusher README](../message-pusher/README.md#用法)。

## 开发

```bash
cd dotnet
dotnet restore
dotnet build
dotnet test
dotnet run --project src/MessagePusher.Api
```

- 健康检查：`GET /health`、`GET /ready`
- Development 下 Swagger UI：http://localhost:3000/swagger （生产环境不启用）
- 不要在 WSL 与 Windows 之间共用 `bin/`、`obj/`。换系统编译前删掉再 `dotnet restore`。

`launchSettings.json` 默认 `http://localhost:3000`、`ASPNETCORE_ENVIRONMENT=Development`。

## 文档

| 文档 | 内容 |
|---|---|
| [dotnet-design.md](./docs/dotnet-design.md) | 分层、实体、管道、配置、分阶段计划 |
| [dotnet-migration-blueprint.md](./docs/dotnet-migration-blueprint.md) | 源项目梳理与迁移蓝图 |
| [dotnet-design-review.md](./docs/dotnet-design-review.md) | 设计评审 |
| [migration-checklist.md](./docs/todos/migration-checklist.md) | 实现核对清单 |

## License

AGPL-3.0，见 [LICENSE.txt](./LICENSE.txt)。原版 Go 项目为 MIT。
