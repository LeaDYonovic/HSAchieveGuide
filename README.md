# 炉石成就攻略 HSAchieveGuide

一个配合 [Firestone](https://github.com/Zero-to-Heroes/firestone) 使用的
Windows 炉石传说成就查看与管理工具。它把官方成就层级、账号完成进度、
卡牌收藏和社区攻略整理到同一个桌面界面中。

> 非 Blizzard Entertainment 或 Firestone 官方产品。项目仍可能存在
> 漏项、版本兼容问题和界面 Bug，欢迎提交 Issue 或 Pull Request。

## 主要功能

- 按官方分类、三级分类和职业查看成就完成度。
- 按官方 `nextTierId` 合并多阶段成就，官方分类与追踪列表集中显示阶段完成数、最终阶段进度和整组成就点数。
- 成就明细与追踪列表显示进度百分比，点击列标题可按百分比数值排序。
- 追踪列表默认按最近进度更新时间倒序；刷新时检测进度变化并持久保存更新时间。
- 将官方“经典（已停运）”成就单独标记为“停用”，不列入未完成；
  Firestone 原始完成数和账号当前成就点数保持不变。
- 查看成就进度、阶段要求、成就点数和关联卡牌收藏。
- 查看、筛选个人卡牌收藏。
- 显示本地社区攻略、卡组代码和原始来源链接。
- 主程序直接读取本机 Firestone 的账号成就进度，不启动外部辅助程序；
  同时重新读取 Firestone 已写入本机的收藏数据。
- Firestone 暂时不可用时使用上次成功读取的进度缓存，并以随包的官方
  成就结构作为离线基准。
- 不包含攻略服务器、攻略上传或管理员审核功能。

## 运行要求

- 64 位 Windows 10 或 Windows 11。
- .NET Framework 4.8（多数 Windows 10/11 电脑已包含或可在系统功能中启用）。
- 已安装并运行 Firestone；更新账号数据时建议同时启动炉石传说。

## 使用方法

1. 从 Releases 下载发布包并完整解压。
2. 双击 `HSAchieveGuide.exe`，首次解析数据可能需要 10 至 30 秒。
3. 如果没有自动找到 Firestone 数据，点击“选择目录”。
4. 数据目录通常是：

   `%APPDATA%\Overwolf\lnknbakkpommmjjdnelmfbjjdbocfpnpbkijjnob`

   正确目录通常包含 `collection.json`，以及 `cards_zhCN.gz.json` 或
   `localization-zhCN.json`。不要选择
   `%LOCALAPPDATA%\Overwolf\Extensions\...`。

### 主动触发 Firestone 更新

- 成就：完成一局任意模式（休闲模式也可以），回到主界面等待 2 至 5 秒，
  再点击本工具的“刷新数据”。
- 收藏：开包、制作、分解或领取卡牌后等待 5 至 8 秒，再点击“刷新数据”。

本工具读取 Firestone 已写入本机的数据，不能代替 Firestone 主动向游戏
请求更新。

## 从源码构建

构建脚本会从 NuGet 官方源下载固定版本的 Roslyn 编译器，并使用本机
.NET Framework 4.x 参考程序集进行编译：

```powershell
PowerShell -ExecutionPolicy Bypass -File .\scripts\compile-HSAchieveGuide.ps1
```

输出位于 `dist/`。制作一个带版本号的分享压缩包：

```powershell
PowerShell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 20260816-01
```

构建需要 Windows PowerShell 5.1 或 PowerShell 7、网络连接和 .NET
Framework 4.x。编译器缓存保存在 `.build/`，不会提交到仓库。

## 项目结构

```text
assets/                       Logo
data/                         构建时嵌入或随包分发的数据
data/official-calibration/    当前官方成就层级基准
scripts/                      编译与发布脚本
src/HSAchieveGuide.cs         主程序
src/FirestoneRuntimeBridge.cs Firestone 本地成就读取桥接层
```

打包脚本会在生成 ZIP 后调用 Microsoft Defender 扫描主程序和压缩包；
任一文件未通过时会停止发布。

## 数据与隐私

- 程序读取本机 Firestone/炉石传说数据。
- 成就刷新由主程序直接加载本机 Firestone 组件完成，不会启动其他程序。
- 不会把账号成就、收藏或本机路径上传到本项目的服务器；本项目没有此类服务器。
- 点击攻略来源时会使用默认浏览器打开第三方网页。
- 请勿把运行后生成的 `json/mindvision-export/`、日志或目录配置公开提交。
  `.gitignore` 已默认排除这些文件。

## 开源许可

原创程序代码和构建脚本使用 [MIT License](LICENSE)。游戏数据、商标、
社区攻略和第三方内容不适用 MIT，详见 [NOTICE.md](NOTICE.md)。

## 鸣谢

- 攻略作者“活着就好”及其[营地主页](https://www.iyingdi.com/tz/people/11839081)。
- Why、断点、渔樵渡及 QQ 群 562175526 的资料整理与讨论支持。
- [Zero to Heroes / Firestone](https://github.com/Zero-to-Heroes/firestone)
  提供的优秀炉石伴侣工具及数据基础。
- 本工具由社区个人整理与发布，开发过程中使用了 OpenAI 提供的辅助工具。
