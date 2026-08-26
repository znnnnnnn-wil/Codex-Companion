# 贡献指南

感谢你参与 Codex Companion。提交代码前，请先阅读项目文档，并确保变更不会扩大项目声明的能力边界。

## 开发流程

1. 从 `main` 创建功能分支。
2. 保持提交聚焦，每个提交只解决一个问题。
3. 在提交前运行与改动相关的测试、Lint 和构建检查。
4. 推送分支并创建 Pull Request，不要直接修改 `main`。

建议使用以下分支命名：

- `feat/<short-description>`：新功能
- `fix/<short-description>`：问题修复
- `refactor/<short-description>`：不改变行为的重构
- `docs/<short-description>`：文档变更
- `chore/<short-description>`：工具链、依赖或仓库维护

## 提交信息

提交信息遵循 Conventional Commits：

```text
<type>(<scope>): <imperative summary>
```

常用类型包括 `feat`、`fix`、`refactor`、`docs`、`test`、`build` 和 `chore`。标题使用英文祈使句，不超过 72 个字符。

## Pull Request

- 标题与提交信息保持一致，清楚说明变更目的。
- 描述中说明背景、主要改动、验证方式和已知限制。
- UI 变更附上截图或录屏；协议、安全和部署变更同步更新文档。
- 不提交 `.env`、凭据、私钥、构建目录、日志或本地调试产物。
- 保持 PR 小而完整；无关格式化和大范围重命名请单独提交。
