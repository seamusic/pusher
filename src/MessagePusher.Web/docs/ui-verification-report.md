# MessagePusher.Web 功能页实施核查报告

- **核查日期**：2026-09-19
- **核查对象**：`dotnet/src/MessagePusher.Web`（Blazor InteractiveServer / net10.0 / MudBlazor 9.10）
- **核查方式**：源码逐页审阅 + Razor 生成代码核验 + 全量重建（`--no-incremental`）+ 实际启动应用做 HTTP 冒烟测试

## 一、结论

**通过（已修复 2 个缺陷）**。功能页已从占位页升级为真实功能实现，路由、数据绑定、API 契约均正确；
全量重建 **0 错误 0 警告**；实际启动后各关键路由返回符合预期。

## 二、已修复缺陷

### P1-1　跨页共享样式定义在单页内联 `<style>` 中，导致深链接样式丢失

- **现象**：`.panel`（卡片边框/圆角/内边距）只定义在 `Pages/Home.razor` 的内联 `<style>` 里，却被 9 个页面
  （Message / Channel / ChannelEdit / Webhook / WebhookEdit / User / UserEdit / Setting / Editor）使用。
- **影响**：先访问首页再点菜单 → 正常；直接打开或刷新（F5）`/channel` 等页面 → Home 组件不渲染，
  `.panel` 规则不存在于 DOM → 卡片退化为无边框、无内边距的一整块白纸。属于隐蔽且随导航路径变化的不一致。
- **修复**：将 `.panel / .panel-head / .kv-* / .quick-* / .footer-note / .stat-card* / .coming-soon* / .tone-* / .msg-body`
  统一提升到 `wwwroot/app.css`；删除 `Home.razor`、`StatCard.razor`、`ComingSoon.razor`、`Message.razor` 中重复的内联样式块。
- **验证**：`/` 首页 HTML 中 `panel`、`stat-card` 标记存在且样式来自全局表。

### P1-2　匿名访问 `[Authorize]` 页面返回 HTTP 500

- **现象**：对 `/channel`、`/user`、`/message`、`/setting` 等受保护路由直接发起 HTTP 请求（F5 / 书签 / 分享链接）返回 500。
- **根因**：页面上的 `[Authorize]` 同时成为端点的授权元数据，ASP.NET Core 授权中间件对匿名请求执行 Challenge；
  而 `Program.cs` 只调用了 `AddAuthorizationCore()`，未注册任何认证服务，于是抛出
  `InvalidOperationException: Unable to find the required 'IAuthenticationService' service`。
- **修复**：`Program.cs` 注册 Cookie 方案（`LoginPath = "/login"`、`AccessDeniedPath = "/login"`）并加入
  `app.UseAuthentication(); app.UseAuthorization();`。组件级授权仍由 `AuthorizeRouteView` + `RedirectToLogin` 处理，API 会话机制不变。
- **验证**：`curl -o NUL -w "%{http_code} %{redirect_url}" /channel` → `302 -> /login?ReturnUrl=%2Fchannel`；
  跟随重定向后的页面内容为登录页（含「登录控制台」，不含「推送通道」）。

## 三、其他改进

| 项 | 说明 |
| --- | --- |
| `Error.razor` 仍是原始英文模板 | 原为 Bootstrap 风格 `text-danger` 英文栈信息，与全站气质完全不符。已重写为中文、居中卡片、带请求 ID 与「返回首页 / 重新加载」。 |
| `StatTone` 枚举位置别扭 | 原为 `StatCard` 组件内嵌枚举，调用方须写 `StatCard.StatTone.Primary`。已提升为顶层 `Components/Common/StatTone.cs`。 |
| 图标按钮缺少无障碍名称 | 为各表格行的纯图标操作按钮（查看/重发/编辑/删除/测试/复制/启停/升降级）补充 `aria-label`；为两处「随机令牌」装饰按钮补充 `AdornmentAriaLabel`（与 Login 页既有规范一致）。 |
| 列表自动刷新闪烁 | `Message.razor` 每 10 秒自动刷新会切换 `_loading`，导致整表 Loading 遮罩周期性闪烁。已改为 `silent: true` 静默刷新（静默时也不再弹错误提示）。 |
| 抽屉页脚定位 | 原为 `position: absolute`，内容滚动时可能与导航项重叠。改为抽屉内容纵向弹性布局 + `margin-top: auto`。 |
| 无效 ARIA 撤回 | 核查中一度为 `MudTable` / `MudMenu` 添加 `aria-label`，经查其未匹配特性落在无语义的容器 `div` 上（role=generic 不支持命名），属无效 ARIA，已撤回。 |

## 四、经核验无误（勿重复怀疑）

- **路由无冲突**：`/channel` 与 `/channel/add|edit/{id}`、`/webhook` 与 `/webhook/add|edit/{id}`、
  `/user` 与 `/user/add|edit|edit/{id}` 均由独立文件声明，无重复 `@page`。
- **Razor 字面量陷阱不存在**：`Value="field.Get(_form)"`、`Value="_email"` 等无 `@` 写法，
  因 `MudTextField<T>` 为泛型推断组件，生成代码中它们是 **C# 表达式**而非字面量字符串（经 `/p:EmitCompilerGeneratedFiles=true`
  导出生成代码确认，生成片段无引号包裹）。
- **`InputAttributes` 参数不存在**：MudBlazor 9.10 的 `MudBaseInput<T>` 无该参数（MUD0002 分析器 + 包内 XML 文档双重确认），
  已撤回相关用法；`.mud-*` DOM 结构不受影响。
- **`PushAsync` 契约正确**：Go 侧 `controller/message.go:181-185` 返回 `{success, message, uuid}`，与 `PushResult` 完全对应。
- **`ComingSoon` 仍在使用**：`About.razor` 用于"暂无关于内容"空状态，属合理复用，非遗留占位。

## 五、待决策事项（未改动）

1. **刷新即掉登录态（P2，架构性）**：`Program.cs` 中 `HttpClient` 为 Scoped + 每次新建 `CookieContainer`，
   而作用域即电路；F5 后会创建新电路与新 Cookie 容器 → `Api.SelfAsync()` 无会话 → 用户需重新登录。
   如需"刷新保持登录"，应把 API 会话凭据持久化（如写入受保护的浏览器 Cookie 或服务端会话存储）。
2. **搜索框无可见 Label（P3，可访问性）**：现依赖 `placeholder` 作为可访问名称回退，
   可满足自动检查，但 WCAG 更推荐显式标签；若需严格符合，可为搜索框补 `Label`（会带来浮动标签的视觉变化）。
3. **登录页未接入主题令牌（P3）**：`Login.razor.css` 中的深色渐变配色为硬编码色值，与其他页面的
   `--mud-palette-*` 体系相互独立。属有意为之的独立视觉，是否统一由产品定。
4. **后台刷新循环的错误面（P3）**：`Message.razor` 的 `PeriodicTimer` 循环仅捕获 `OperationCanceledException`，
   组件销毁顺序异常时理论上可能抛出 `ObjectDisposedException`；可加一层兜底 catch。

## 六、核查覆盖范围

重读并核对文件 21 个：4 个布局/入口（MainLayout、LoginLayout、App、Routes + Program）、
13 个页面（Home、Message、Editor、Channel、ChannelEdit、Webhook、WebhookEdit、User、UserEdit、Setting、About、NotFound、Error）、
3 个公共组件（PageHeader、StatCard、ComingSoon）、1 个枚举（StatTone）、2 个样式源（app.css、MainLayout.razor.css）、
1 个 API 客户端与 1 个模型文件（ApiClient、Models）。
