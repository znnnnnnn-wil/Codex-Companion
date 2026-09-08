# 手机查看 Codex 剩余额度

打开左上角会话列表，在底部的“剩余用量”卡片查看电脑端 Codex 账户的剩余百分比和重置时间。支持 5 小时、1 周以及服务端返回的其他周期，多个额度类别分别显示。卡片标明更新时间，页面可见且电脑在线时每分钟刷新，也可以手动刷新。重置时间使用手机本地时区。

数据来自 Bridge 所使用的 Codex CLI 的 ChatGPT 登录账户，可能与其他电脑或不同 CODEX_HOME 下的账户不同。不会读取当前聊天的 token 数作为账户额度，也不代表 API 账单余额。API Key 登录或服务端未提供额度时显示不可用/暂无数据。

升级 Relay、Web 和 Windows Bridge 至 v0.1.7 或以上，并确认 Codex CLI 支持 [官方 app-server 的 account/rateLimits/read](https://learn.chatgpt.com/docs/app-server)。已有配对可继续使用。

## 只读协议

已配对网页发送 `account.rateLimits.request`，Bridge 调用 `account/rateLimits/read`，通过 `account.rateLimits.response` 返回：

```json
{"buckets":[{"limitId":"codex","limitName":null,"primary":{"usedPercent":4,"windowDurationMins":300,"resetsAt":1800000000},"secondary":null}],"fetchedAt":1799990000}
```

优先使用 `rateLimitsByLimitId`，兼容旧版 `rateLimits`。剩余百分比为 `100 - usedPercent`，限制在 0–100；缺失值保留为未知。`resetsAt`、`fetchedAt` 均为 Unix 秒。已到重置时间的数据标为“待刷新”，不会推断额度已经恢复。

只投影额度字段，不转发账户邮箱、凭据或原始错误。Relay 按设备与 requestId 将响应发回原始网页，不广播到其他网页。协议不开放账户登录、退出、购买或重置额度操作。断开连接立即清除旧值；读取超时/失败暂停自动重试，手动刷新或重新连接可恢复，兼容未升级的 Bridge。
