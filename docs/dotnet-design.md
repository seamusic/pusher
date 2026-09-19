# message-pusher .NET (C#) 重写设计文档

> 版本：v1.1
> 依据：[dotnet-migration-blueprint.md](./dotnet-migration-blueprint.md)（含附录 B、C 源码核验结果）
> 目标：以 C#/.NET 完整重写 Go 版 message-pusher，做到 **API、前端、数据三层完全兼容**，可灰度切换上线。

---

## 1. 目标与兼容性契约

### 1.1 目标

1. 用 ASP.NET Core 重写后端，前端 React SPA **零改动**复用（构建产物放 `wwwroot`）。
2. 复用现有数据库（SQLite 默认 / MySQL），老库可无损升级，老用户密码可直接登录。
3. 对外 API 契约完全一致：路径、字段名（snake_case）、响应体结构、HTTP 状态码语义。

### 1.2 兼容性契约（贯穿全文，不可破坏）

| 契约 | 说明 |
|---|---|
| 响应体 | 统一 `{ "success": bool, "message": string, "data": ... }` |
| 字段命名 | snake_case（`display_name`、`save_message_to_database`、`created_time`…），用 `JsonPropertyName` / `Column` 显式指定 |
| HTTP 语义 | 业务失败返回 **200 + success:false**；未登录/无效 token 才 401；限流 429；Webhook 未找到 404/403/400 |
| ID 形态 | `link`/`token` 用 `Guid.NewGuid().ToString("N")`（32 位无横线） |
| 密码 | BCrypt（与 Go `x/crypto/bcrypt` 的 `$2a$`/`$2b$` 互认，`DefaultCost=10`） |
| 数据表 | 5 张表、列名/类型与现网一致（详见 §8） |

---

## 2. 技术栈与包选型

- **目标框架**：.NET 10 (LTS)。
- **Web**：ASP.NET Core（Controllers 路由，最小可行，不用 Minimal API）。
- **ORM**：EF Core（`Microsoft.EntityFrameworkCore.Sqlite` / `Pomelo.EntityFrameworkCore.MySql`）。
- **认证**：`Microsoft.AspNetCore.Authentication.Cookies` + `IDistributedCache`（内存或 Redis）。
- **Redis**：`StackExchange.Redis`（可选，限流 + 会话）。
- **密码**：`BCrypt.Net-Next`。
- **Markdown**：`Markdig`（GFM + 脚注，等价 goldmark）。
- **邮件**：`MailKit`。
- **校验**：`FluentValidation.AspNetCore`（配合 DataAnnotations）。
- **日志**：`Serilog.AspNetCore`（文件 + 控制台）。
- **限流**：自实现（对齐 Go 的滑动队列语义，见 §9），不依赖内置 `AddRateLimiter` 的窗口限流器。
- **Swagger（可选）**：`Swashbuckle.AspNetCore`。

> 说明：依赖映射完整对照见蓝图 §3；蓝图 12.2 中提到的 `Microsoft.AspNetCore.RateLimiting` 内置限流器**不采用**（语义与 Go 的「最近 N 次请求队列」不等价，见附录 C.2）。

---

## 3. 解决方案结构

```
MessagePusher.sln
├─ src/MessagePusher.Domain
│   ├─ Entities/        User, Channel, Message, Option, Webhook
│   ├─ Enums.cs         UserStatus, MessageSendStatus, ChannelStatus, WebhookStatus
│   ├─ Constants.cs     Roles, ChannelType, SendEmailToOthers / SaveMessageToDatabase 等等
│   └─ Exceptions/      UnsupportedChannelException, BusinessException ...
├─ src/MessagePusher.Application
│   ├─ Abstractions/    各 Service 接口、IChannelProvider、IRepository 接口
│   ├─ Services/        PushService, MessageService, ChannelService, UserService,
│   │                   WebhookService, OptionService, AuthService, VerificationService
│   ├─ Channels/        IChannelProvider + 16 个实现 + ChannelProviderFactory
│   ├─ Messaging/       AsyncMessageQueue(Channel<int>), AsyncMessageWorker : BackgroundService
│   ├─ Realtime/        SseBroker, WebSocketClientManager
│   └─ Tokens/          TokenStore : BackgroundService
├─ src/MessagePusher.Infrastructure
│   ├─ Persistence/     AppDbContext, IAppDbContext, Repositories
│   ├─ Cache/           IDistributedCache 装配、Redis 连接
│   ├─ Email/           SmtpMailSender (MailKit)
│   ├─ Markdown/        MarkdownMarkup (Markdig)
│   ├─ Crypto/          BcryptPasswordHasher, GuidGenerator
│   └─ Options/         SystemOptionSource (内存 OptionMap + DB 双写)
├─ src/MessagePusher.Api
│   ├─ Controllers/     UserController, MessageController, ChannelController,
│   │                   WebhookController, OptionController, PushController,
│   │                   MiscController, AuthController
│   ├─ Middleware/      ExceptionHandling, SseHeaders, TurnstileCheck
│   ├─ ApiResult.cs     统一响应
│   └─ wwwroot/         React 构建产物 + message.html
└─ tests/
    ├─ MessagePusher.Domain.Tests
    ├─ MessagePusher.Application.Tests
    └─ MessagePusher.Api.IntegrationTests
```

分层依赖方向：`Api → Application → Domain`，`Api → Infrastructure`，`Application → Domain`，仅 `Infrastructure` 依赖具体外部库（EF/Redis/MailKit/Markdig/BCrypt）。

---

## 4. Domain 层设计

### 4.1 实体

#### `User`

| 属性 | 类型 | 映射 | 说明 |
|---|---|---|---|
| Id | int | PK | |
| Username | string | `unique;index`, ≤12 | |
| Password | string | `not null`, bcrypt | 明文 8–20，禁用持久化到 DTO |
| DisplayName | string | `index`, ≤20 | snake_case: `display_name` |
| Role | int | default 1 | 见 Roles |
| Status | int | default 1 | |
| Token | string | | 全局推送令牌 |
| Email | string | `index`, ≤50 | |
| GitHubId | string | `github_id`, index | |
| WeChatId | string | `wechat_id`, index | |
| Channel | string | | 默认通道名 |
| SendEmailToOthers | int | `send_email_to_others`, default 0 | 0 未设 / 1 允许 / 2 不允许 |
| SaveMessageToDatabase | int | `save_message_to_database`, default 0 | 同上 |

非持久化入参：`VerificationCode`（`[NotMapped]`）。

#### `Channel`

| 属性 | 类型 | 映射 |
|---|---|---|
| Id | int | PK |
| Type | string | `varchar(32)` |
| UserId | int | `user_id`，复合唯一索引 `name_user_id` |
| Name | string | `varchar(32)`，复合唯一索引 `name_user_id` |
| Description | string | |
| Status | int | default 1 |
| Secret | string | index |
| AppId | string | `app_id` |
| AccountId | string | `account_id` |
| Url | string | `url` |
| Other | string | |
| CreatedTime | long | `created_time`, bigint（Unix 秒） |
| Token | string? | 通道维度鉴权令牌，nullable |

#### `Message`

| 属性 | 类型 | 映射 |
|---|---|---|
| Id | int | PK |
| UserId | int | `user_id`, index |
| Title | string | |
| Description | string | |
| Content | string | |
| Url | string | `url` |
| Channel | string | 通道**名**（非类型） |
| Timestamp | long | bigint（Unix 秒） |
| Link | string | unique index（32 位 UUID） |
| To | string | `to`，`|` 分隔，`@all` |
| Status | int | default 0, index |
| RenderMode | string? | `render_mode` |

非持久化：`Token`、`HtmlContent`、`Async`、兼容字段 `Short`/`Desp`/`OpenId`（`[NotMapped]`，仅入参）。

#### `Option`

| 属性 | 类型 | 映射 |
|---|---|---|
| Key | string | PK |
| Value | string | bool 存 "true"/"false" |

#### `Webhook`

| 属性 | 类型 | 映射 |
|---|---|---|
| Id | int | PK |
| UserId | int | `user_id`, index |
| Name | string | `varchar(32)`, index |
| Status | int | default 1 |
| Link | string | `char(32)`, unique index |
| CreatedTime | long | `created_time`, bigint |
| ExtractRule | string | `extract_rule`（JSON：变量名 → gjson 路径） |
| ConstructRule | string | `construct_rule`（JSON，`$变量` 占位） |
| Channel | string | `varchar(32)` |

### 4.2 常量与枚举

```csharp
public static class Roles { public const int Guest = 0, Common = 1, Admin = 10, Root = 100; }
public enum UserStatus { NonExisted = 0, Enabled = 1, Disabled = 2 }
public enum MessageSendStatus { Unknown = 0, Pending = 1, Sent = 2, Failed = 3, AsyncPending = 4 }
public enum ChannelStatus { Unknown = 0, Enabled = 1, Disabled = 2 }
public enum WebhookStatus { Unknown = 0, Enabled = 1, Disabled = 2 }

public static class ChannelType {
    public const string Email = "email";
    public const string WeChatTestAccount = "test";
    public const string WeChatCorpAccount = "corp_app";
    public const string Corp = "corp";
    public const string Lark = "lark";
    public const string LarkApp = "lark_app";
    public const string Ding = "ding";
    public const string Bark = "bark";
    public const string Client = "client";
    public const string Telegram = "telegram";
    public const string Discord = "discord";
    public const string OneBot = "one_bot";
    public const string Group = "group";
    public const string Custom = "custom";
    public const string TencentAlarm = "tencent_alarm";
    public const string None = "none";
}

public static class UserPreference {  // send_email_to_others / save_message_to_database 语义
    public const int Unset = 0, Allowed = 1, Disallowed = 2;
}
```

系统默认值（启动时可被 options 表覆盖，见 §11）：

```
SystemName = "消息推送服务"; ServerAddress = "http://localhost:3000";
SMTPPort = 587; SQLitePath = "message-pusher.db";
ItemsPerPage = 10; VerificationValidMinutes = 10;
TokenStoreExpirationSeconds = 2 * 55 * 60;   // 110 分钟
```

限流参数（秒）：

| 名称 | 次数 | 窗口(秒) | 前缀 |
|---|---|---|---|
| GlobalApiRateLimit | 60 | 180 | GA |
| GlobalWebRateLimit | 60 | 180 | GW |
| CriticalRateLimit | 20 | 1200 | CT |
| Upload / Download | 10 | 60 | UP / DW（预留） |

### 4.3 领域异常

```csharp
public class BusinessException : Exception { public BusinessException(string msg) : base(msg) {} }   // → 200 + success:false
public class UnauthorizedException : Exception {}                                                     // → 401
public class UnsupportedChannelException : BusinessException { ... }                                  // 通道不支持
```

---

## 5. Infrastructure 层设计

### 5.1 DbContext（显式 snake_case，杜绝命名策略差异）

```csharp
public sealed class AppDbContext : DbContext {
    public DbSet<User> Users => Set<User>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Option> Options => Set<Option>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();

    protected override void OnModelCreating(ModelBuilder b) {
        b.Entity<User>(e => { ... });
        b.Entity<Channel>(e => {
            e.HasIndex(x => new { x.Name, x.UserId }).IsUnique();      // name_user_id
            e.Property(x => x.Type).HasColumnType("varchar(32)");
            e.Property(x => x.Name).HasColumnType("varchar(32)");
            e.Property(x => x.CreatedTime).HasColumnType("bigint");
        });
        b.Entity<Message>(e => {
            e.HasIndex(x => x.Link).IsUnique();
            e.HasIndex(x => x.Status);                                    // messages.status 有普通索引（蓝图 §5.3）
            e.Property(x => x.Timestamp).HasColumnType("bigint");
            e.Property(x => x.RenderMode).HasColumnName("render_mode");
        });
        b.Entity<Webhook>(e => {
            e.HasIndex(x => x.Link).IsUnique();
            e.Property(x => x.Link).HasColumnType("char(32)");
            e.Property(x => x.CreatedTime).HasColumnType("bigint");
        });
    }
}
```

要点（来自蓝图 12.5）：
1. 全部用 `[Column("snake_case")]` / `HasColumnName` 显式指定，避免依赖命名策略。
2. `char(32)`（webhooks.link）必需显式 `HasColumnType`，MySQL/EF 默认映射不同。
3. 复合唯一索引 `channels(name, user_id)`、唯一索引 `messages.link`、`webhooks.link` 必须显式配置。
4. （参考蓝图 12.5）**不使用** `EnsureCreated`/运行时 AutoMigrate，用 `dotnet ef migrations` 生成可回滚脚本。

### 5.2 Repository

每个聚合一个 Repository，方法一一对应 Go `model/*.go` 的查询（含字段白名单语义）：

```csharp
public interface IUserRepository {
    Task<IReadOnlyList<User>> GetAllAsync(int offset, int count);      // 只 select 白名单（8 字段，见下表）
    Task<IReadOnlyList<User>> SearchAsync(string keyword);
    Task<User?> GetByIdAsync(int id, bool selectAll);
    Task<User?> GetByUsernameAsync(string username);
    Task AddAsync(User user);
    Task UpdateAsync(User user, bool updatePassword);
    Task DeleteAsync(int id);
    Task<bool> EmailTakenAsync(string email);
    Task<bool> UsernameTakenAsync(string username);
    ...
}
```

**字段白名单等价性（关键）**：用户查询存在三套不同的 Select 白名单，必须分别锁定，不可混用（混用会导致泄漏 `token`/`password` 或字段缺失）：

| 查询 | 白名单 | 用途 |
|---|---|---|
| `GetUserById(selectAll=false)` | `id, username, display_name, role, status, email, wechat_id, github_id, channel, token, save_message_to_database`（含 token/channel，不含 password、send_email_to_others） | `/api/user/self`、`/api/user/:id` |
| `GetAllUsers` | `id, username, display_name, role, status, email, send_email_to_others, save_message_to_database`（不含 token/channel/wechat_id/github_id） | 用户管理列表 |
| `SearchUsers` | `id, username, display_name, role, status, email` | 用户搜索 |

EF 用 `.Select(...)` 显式投影，避免把 `password`/`token` 带入响应体。更新类操作（`UpdateSelf`/`UpdateUser`）同样有字段白名单（如排除 `role`/`status`/`token`），实现时逐个对照 `model/user.go`。

### 5.3 其它适配器

- **`IPasswordHasher`**：`BCrypt.Net.BCrypt.HashPassword` / `Verify`（确认 `$2a$`/`$2b$` 互通）。
- **`IGuidGenerator`**：`Guid.NewGuid().ToString("N")`。
- **`IMarkdownRenderer`**：Markdig 管道 `UseAdvancedExtensions()`（GFM + Footnotes）。
- **`IEmailSender`**：MailKit，587 STARTTLS / 465 隐式 TLS（**注意：生产应校验证书，Go 版 `InsecureSkipVerify=true` 是问题项**）。
- **时间戳**：`DateTimeOffset.UtcNow.ToUnixTimeSeconds()`。
- **gjson 等价**：`System.Text.Json.JsonDocument` + 自实现 gjson 路径解析（支持 `foo.bar`、`#` 数组查询等 Webhook 用到的子集）。

---

## 6. Application 层设计

### 6.1 Service 清单

| Service | 职责 |
|---|---|
| `PushService` | `/push/:username` 主流程、`authMessage`、`saveAndSend` |
| `MessageService` | 消息 CRUD、状态查询、重发、SSE 推送 |
| `ChannelService` | 通道 CRUD、secret 白名单、TokenStore 联动 |
| `UserService` | 用户 CRUD、ManageUser 9 个 action、角色策略 |
| `WebhookService` | Webhook CRUD、`extract/construct` 规则解析 |
| `OptionService` | 系统选项读（脱敏）写、依赖校验 |
| `AuthService` | 登录/注册/登出、OAuth、邮箱验证、密码重置 |
| `VerificationService` | 验证码生成/校验（内存或 Redis） |

### 6.2 统一响应 `ApiResult<T>`

```csharp
public sealed class ApiResult<T> {
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")]    public T? Data { get; set; }
    public static ApiResult<T> Ok(T? data = default) => new() { Success = true, Data = data };
    public static ApiResult<T> Fail(string msg)      => new() { Success = false, Message = msg };
}
```

> 注意：`success` 字段与 `Success` 属性名大小写冲突（System.Text.Json 大小写不敏感会重复映射）。建议用 `JsonPropertyName("success")` 配 `[JsonIgnore]` 或改为 `IsSuccess` 属性 + 显式 `JsonPropertyName`。实现时以单元测试锁定序列化结果。

> 例外 —— 有两个响应的字段**不在 `data` 内**，直接在顶层：
> - `/push/:username` → `{ "success": true, "message": "", "uuid": "<link>" }`
> - `/api/message/status/:link` → `{ "success": true, "message": "", "status": <int> }`
> 分别用独立 DTO `PushResult` / `MessageStatusResult` 表达，勿套 `ApiResult<T>`。

### 6.3 通道 Provider（策略模式）

```csharp
public interface IChannelProvider {
    string Type { get; }
    Task SendAsync(Message message, User user, Channel channel, CancellationToken ct);
}

public sealed class ChannelProviderFactory {
    private readonly IReadOnlyDictionary<string, IChannelProvider> _map;
    public ChannelProviderFactory(IEnumerable<IChannelProvider> providers)
        => _map = providers.ToDictionary(p => p.Type);
    public IChannelProvider Resolve(string type)
        => _map.TryGetValue(type, out var p) ? p : throw new UnsupportedChannelException(type);
}
```

16 个 Provider：`EmailProvider`、`WeChatTestProvider`、`WeChatCorpProvider`、`CorpProvider`、`LarkProvider`、`LarkAppProvider`、`DingProvider`、`BarkProvider`、`ClientProvider`、`TelegramProvider`、`DiscordProvider`、`OneBotProvider`、`GroupProvider`、`CustomProvider`、`TencentAlarmProvider`、`NoneProvider`。

DI 自动扫描注册：`services.Scan(...)` 或手动 `AddSingleton<IChannelProvider, X>(...)`。

各通道的字段支持矩阵、隐藏行为（firme 签名、消息分段、`to` 语义）见蓝图 §10 与附录 B.3.1/B.3.2，实现时逐一对照。

### 6.4 TokenStore（第三方 access_token，BackgroundService）

```csharp
public sealed class TokenStore : BackgroundService {
    // key → ITokenStoreItem；ExpirationSeconds = 2*55*60
    // 每 Max(ExpirationSeconds, 60) 秒后台刷新一次
    // IsShared() 判断凭据是否被多通道复用（复用则不随单通道删除而移除）
    // 通道增/改/删、用户启用/禁用/删除时同步维护
}
```

> 二轮/三轮核验已修正的点：`UpdateChannel` 与 `GetTokenStoreChannelsByUserId` 必须覆盖 `lark_app`（Go 版漏掉，见蓝图 12.8/附录 B.2）；写操作用 `ReaderWriterLockSlim.EnterWriteLock`（Go 版误用 RLock）。

### 6.5 异步队列 + Worker

```csharp
public sealed class AsyncMessageQueue {
    private readonly Channel<int> _ch = Channel.CreateBounded<int>(
        new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.Wait });
    public ValueTask EnqueueAsync(int id, CancellationToken ct) => _ch.Writer.WriteAsync(id, ct);
    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken ct) => _ch.Reader.ReadAllAsync(ct);
}

public sealed class AsyncMessageWorker : BackgroundService {
    // 启动时回填 DB 中 status=4 (AsyncPending) 的 id（等价 LoadAsyncMessages）
    // 循环：取 id → 查消息 → 查用户 → 查通道 → Send → 更新状态(Sent/Failed)
}
// 注册 2 个 worker 实例（等价 Go 的 2 个 goroutine sender）
```

### 6.6 实时能力

- **`SseBroker`**：每用户一个 `Channel<T>`（缓冲 10）；`GET /api/message/stream` 订阅，以 `message` 事件写流。
- **`WebSocketClientManager`**：按 `channelName:userId` 定位连接；保活 `pongWait=60s, pingPeriod=54s, writeWait=10s, maxMessageSize=512`；同 key 新连接挤旧连接（先发「被挤下线」bye 再 close），新连接发「客户端连接成功！」hello；关闭时用 `timestamp` 防误删。

### 6.7 SystemOptionService（内存 + DB 双写）

等价 Go 的 `OptionMap` + `RWMutex`：`ConcurrentDictionary<string,string>` 或 `Dictionary + ReaderWriterLockSlim`；`InitOptionMap()` 先用默认值，再用 DB 覆盖；`UpdateOption` 改库 + 改内存；`GET /api/option` 过滤含 `Token`/`Secret` 的 key。推荐封装 `IOptionsMonitor<SystemOptions>` 自定义源。

---

## 7. Api 层设计

### 7.1 请求管道（Program.cs 推荐顺序）

```csharp
UseExceptionHandling → UseRouting → UseSession/UseAuthentication(UseAuthorization)
→ UseRateLimiter(自定义中间件) → UseStaticFiles(wwwroot，含 SPA) → UseWebSockets
→ MapControllers → MapFallbackToFile("index.html")
```

> 蓝图 7 的坑：Go 版把 `Cache-Control: max-age=604800` 加在所有 web 请求（含 HTML 兜底）；迁移只对带 hash 静态资源设长缓存，避免控制台更新不刷新。

### 7.2 中间件

- `ExceptionHandlingMiddleware`：捕获 `BusinessException`→200+success:false；`UnauthorizedException`→401；其余→500。
- `SseHeadersMiddleware` 或 Controller 内直接写头（`Content-Type: text/event-stream`、`no-cache`、`X-Accel-Buffering: no`）。
- `TurnstileCheckMiddleware`：仅 `TurnstileCheckEnabled` 时启用；通过后 session 存 `turnstile=true` 复用（**必须保持，否则前端重复弹校验**）。
- 自定义 `RateLimitMiddleware`（见 §9，替换内置 `AddRateLimiter`）。

### 7.3 Controller → Service 映射

按蓝图 §8 全清单逐条落地，路由表直接沿用：

- `MiscController`：`/api/status`、`/api/notice`、`/api/about`
- `AuthController`：登录/登出/注册/重置密码/邮箱验证码/GitHub & 微信 OAuth/绑定
- `UserController`：self、token、管理面（Admin/Root）
- `MessageController`：列表/详情/搜索/重发/删除/清空/SSE/状态查询
- `ChannelController`：CDRUD（list 默认 Omit secret；`?brief=1`；`?status_only=`）
- `WebhookController`：CDRUD + 搜索（关键字前缀 `{ServerAddress}/webhook/` 需剥离为真实 name）
- `OptionController`（Root）：读（脱敏）/更新
- `PushController`：`GET/POST /push/:username`
- `WebhookTriggerController`：`POST /webhook/:link`
- `ClientController`（WebSocket）：`GET /api/register_client/:username`（升级 WS，见 §6.6）

### 7.4 认证与授权

- `AddAuthentication(CookieAuthenticationDefaults)` + Claims：`Id`/`Username`/`Role`/`Status` 四 Claim（等价 session 四项）。
- 自定义 `RequireRoleAttribute` 或 Policy：`RequireRole(Roles.Common/Admin/Root)`，映射 Go `UserAuth`/`AdminAuth`/`RootAuth`。
- `UserPolicy` 服务收敛「不能操作同级或更高角色」「仅 Root 可提升管理员」等散落规则（蓝图 12.3/8.3）。
- Cookie 参数：`HttpOnly=true`、`MaxAge=30天`、`Path=/`、同名 `session`（可选）。
- **数据保护密钥持久化**：等价 Go `SessionSecret`；未持久化则重启全部会话失效（蓝图 B.3.3，生产必须落盘/Redis）。

---

## 8. 数据模型映射总表

见蓝图 §5（5 张表完整列定义）。迁移实现时逐列对齐；`[Table("...")]`/`HasColumnName` 显式指定。

### 8.1 老库升级策略

1. 严格对齐列名/类型，`char(32)`、`bigint`、`varchar(32)` 显式 `HasColumnType`。
2. `dotnet ef migrations add Init` 生成可回滚脚本（**不要** EnsureCreated）。
3. 双跑期确认写入字段一致；建议灰度切换而非长期双写。

---

## 9. 限流设计（自定义，对齐 Go 语义）

Go 版是「最近 N 次请求的滑动队列」，**不是**窗口计数（附录 C.2）。需自实现：

```csharp
public interface IRateLimiter {
    // 返回 false 表示拒绝（→429）
    bool Check(string key, int maxRequestNum, int durationSeconds);
}

public sealed class MemoryRateLimiter : IRateLimiter {
    // 每 key 维护 List<long>（Unix 秒时间戳），逻辑等价 common/rate-limit.go:
    //   len < max → 追加并放行
    //   len == max → 若最旧 >= duration 则出队一条再追加放行，否则拒绝
    // 后台定时 clearExpiredItems() 清理过期 key（等价 Go 协程）
}
```

Redis 版（`RedisRateLimiter`，启用 Redis 时使用）：

```
key = "rateLimit:{mark}{clientIP}"   mark ∈ {GA,GW,CT,UP,DW}
时间戳格式 yyyy-MM-ddTHH:mm:ss.fffZ
最旧 = LIndex(key, -1)
放行分支（listLen < max，或最旧已过期）：LPush(key, ts)，若满则 LTrim(key, 0, max-1)，Expire 20min
拒绝分支（满且最旧未过期）：仅 Expire 20min + 返回 429，不 LTrim
C# 对应：ListLeftPush / ListGetByIndex(-1) / ListTrim / KeyExpire
```

中间件工厂 `RateLimitMiddleware(mark, num, duration)` 包装上述实现，register 到 `/api`、`/push`、`/webhook`、web 全局。

---

## 10. 核心业务流程

### 10.1 推送主流程（`/push/:username`）

```
1. 解析：GET 用 Query；POST 按 Content-Type 选择 JSON / Form
   字段：title, description(desp/short), content, url, channel, token, to(openid), async, render_mode
2. keepCompatible：description 空→short；content 空→desp；to 空→openid；title 空→SystemName（Go 为 processMessage 首步）
3. 按 username 查用户 → 校验存在 / 未封禁
4. token 来源：POST 时 Body → Query → Authorization: Bearer；GET 时 Query → Authorization
5. 通道解析：channel 空→用户默认 channel→"email"；按 (name,user_id) 查，不存在报错
6. authMessage(messageToken, userToken, channelToken) 判定：
   - userToken 非空 且 messageToken == userToken → 通过（短路）
   - userToken 非空 且 messageToken != userToken → 401  ← 修正蓝图 12.8 漏洞（原实现会误放行撞上 channelToken 的场景）
   - userToken 为空 且 channelToken 非空 → messageToken 必须 == channelToken，否则 401
   - userToken、channelToken 均空 → 通过（无鉴权）
7. render_mode=code → content 包 ``` 代码块
8. saveAndSend：
   - 通道禁用 → 报错
   - link = UUID(N)；url 空 → {ServerAddress}/message/{link}
   - 持久化（全局 flag 或用户 save_message_to_database==1）：
       插库(Pending) → SSE 推送 → 同步发送 / 异步入队(async→AsyncPending) → defer 更新最终状态
   - 否则 link="unsaved"，直接发送（async 报错）
9. 返回 PushResult { success, message:"", uuid:link }（见 §6.2 例外）
```

### 10.2 异步队列（见 §6.5）

启动 `LoadAsyncMessages()` 等价逻辑：worker 启动前回填 `status=4` 的 id 到队列，避免重启丢消息。

---

## 11. 配置设计

| Go 来源 | C# 落点 |
|---|---|
| `REDIS_CONN_STRING` | `ConnectionStrings:Redis` |
| `SESSION_SECRET` | 数据保护密钥 / `AppSettings:SessionSecret` |
| `SQL_DSN` / `SQLITE_PATH` | `ConnectionStrings:Default` + `Database:Provider` |
| `PORT` | `ASPNETCORE_URLS` / Kestrel |
| `GIN_MODE=debug` | `ASPNETCORE_ENVIRONMENT` |
| `CHANNEL_URL_ALLOW_NON_HTTPS` | `AppSettings:ChannelUrlAllowNonHttps` |
| 命令 `--port/--log-dir/--version` | `IConfiguration`（命令行 provider）+ Serilog 文件日志 |
| options 表动态项 | `SystemOptionService`（§6.7） |

SystemOption 写回依赖校验（蓝图 B.3.4）：启用 `GitHubOAuthEnabled` 前校验 `GitHubClientId`；`WeChatAuthEnabled` 前校验 `WeChatServerAddress`；`TurnstileCheckEnabled` 前校验 `TurnstileSiteKey`。

---

## 12. 行为等价性检查清单（落地前过一遍）

统一响应体/HTPP 语义、snake_case、`Guid.ToString("N")`、BCrypt、`GET /api/channel` 不含 secret、secret 空保留原值、持久化判断、`render_mode` 处理、Server 酱兼容字段、限流 key=前缀+IP、SSE 单连接语义、WS 挤下线+保活参数、启动回填 `status=4`、options 脱敏。

完整清单见蓝图 §12.7 与附录 C。

---

## 13. 分阶段落地计划

| 阶段 | 目标 | 验收 |
|---|---|---|
| P0 骨架 | 分层方案、配置校验 fail-fast、Serilog、`/health`/`/ready`、优雅停机 | 启动成功，健康检查 200 |
| P1 数据层 | 5 实体 + Migrations + Repository + seed root | sqlite/mysql 双 provider，表结构对账 |
| P2 认证授权 | Cookie 登录/登出/注册/重置/验证码、OAuth、角色策略、Turnstile | 前端不改可登录，权限矩阵一致 |
| P3 推送核心 | `/push/:username`、兼容字段、鉴权、持久化、状态机、异步队列、SSE | Server 酱用例全绿；异步可查状态 |
| P4 通道 | 16 Provider + TokenStore + WS 客户端 | 各通道「测试」发通；token 自动刷新 |
| P5 管理面 | 用户/通道/Webhook/消息 CRUD、选项、公告/关于/页脚、消息渲染页 | 前端全功能可用 |
| P6 生产化 | 限流(含 Redis)、安全头、Docker、CI、`/message/:link`、数据迁移、压测 | 与 Go 行为对齐可切流 |

每阶段完成即编译 + 冒烟（curl 关键端点）+ 前端联调。

---

## 14. 需在实现时一并修正的源码问题

迁移时把这些已知缺陷一并修掉（详见蓝图 §12.8 / 附录 B.2 / 附录 C.1），而非照搬：

1. `DeleteUser` 失败分支 `success:true`、成功分支无响应（hang）。
2. `AddWebhook` 名称校验文案写错（"通道名称"）。
3. `SearchMessages` 未按 `user_id` 过滤（越权）。
4. `TriggerWebhook` 的 `extract_rule`/`construct_rule` 解析失败状态码不一致。
5. `TokenStoreUpdateChannel` / `GetTokenStoreChannelsByUserId` 漏 `lark_app`。
6. `authMessage` 通道 token 可绕过用户 token 的逻辑漏洞。
7. 邮件 465 端口 `InsecureSkipVerify=true`。
8. `authHelper` 的 `status.(int)` unsafe 断言。
9. `ManageUser` delete 分支未 return + 删除无级联清理（迁移时明确级联策略）。

## 15. 评审修订记录

**v1.1**（依据 [dotnet-design-review.md](./dotnet-design-review.md) 评审结论修订）：

| 评审项 | 类型 | 修订内容 |
|---|---|---|
| P1-1 | 缺陷 | §10.1 补 `title==空 → SystemName`（Go `processMessage` 首步） |
| P1-2 | 缺陷 | §6.2 增加两个顶层响应 DTO `PushResult`（uuid）/ `MessageStatusResult`（status） |
| P1-3 | 缺陷 | §10.1 步骤 6 `authMessage` 改为完整判定真值表 |
| P1-4 | 缺陷 | §5.2 修正 `GetAllAsync` 白名单引用错误，三套白名单分列表格 |
| P1-5 | 缺陷 | §4.1 / §5.1 补 `messages.status` 普通索引 |
| P2-1 | 建议 | §9 修正 Redis 限流 LTrim 位置（放行分支）；内存版补 `clearExpiredItems` |
| P2-5 | 建议 | §7.3 补 WebSocket 注册端点 `/api/register_client/:username` |
| P2-7 | 建议 | §7.3 补 Webhook 搜索的前缀剥离说明 |

> 设计文档到此结束。实现时按章节顺序落地，每个 Provider / Service 用单元测试锁定与 Go 等价的行为。