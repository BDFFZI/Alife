# Alife.Interpreter.Demo

Alife XML 解释器（Interpreter）的纯逻辑验证测试环境。

## 主要功能
- **纯协议解析**：直接通过控制台输入 XML 流，测试 `XmlStreamParser` 的状态机解析。
- **Mock 执行反馈**：内置 `Pet`、`Speech` 和 `System` 的 Mock 处理器，实时打印解析出的指令参数。
- **动态文档验证**：自动生成并展示当前的标签协议文档。
- **自动化测试**：内置 `test` 命令，一键运行多种复杂（嵌套、分段、异常）的 XML 解析用例。

## 运行方式
直接启动项目即可进入交互式测试界面。
