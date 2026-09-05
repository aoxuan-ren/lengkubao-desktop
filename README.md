# 冷库宝 · 电脑端（Windows）

冷库仓储管理台：库存、入库/销售/包装/预售、客户往来、统计报表、会计年度、备份恢复，并作为手持端的 **局域网同步服务**。

面向库房老板/内勤：看连接状态、配对手持机、切财年、对账导出。现场扫码开单用手持端。

## 功能

- **业务**：入库、销售报账、包装、预售、预支扣款、客户余额与对账单
- **查询导出**：入库/包装/销售/预售/流水查询，Excel 导出（EPPlus）
- **统计**：入库等经营统计
- **会计年度**：按年 SQLite 库 `lengkubao_{年}.db`；年终结转、历史归档
- **同步**：TCP 服务端；二维码 + 配对码配对手持机；mDNS / UDP 发现；双向增量与全量下发
- **数据安全**：主库在 `%LOCALAPPDATA%\LengKuBao\data\`；每日自动备份到 `文档\冷库宝自动备份\`（滚动约 14 份）；启动检测库丢失可恢复
- **授权**：RSA 机器码授权（永久 / 1 年 / 3 年）
- **更新**：内置更新检查（若已配置更新源）

## 技术栈

| 项 | 说明 |
|----|------|
| 运行时 | .NET Framework 4.7.2 |
| UI | WinForms |
| 数据库 | SQLite（System.Data.SQLite） |
| 网络 | TouchSocket、mDNS（Makaretu.Dns）、UDP 广播 |
| 其它 | Newtonsoft.Json、QRCoder、EPPlus |

解决方案：`lengkubao.desktop/lengkubao.desktop.sln`  
主项目：`lengkubao.desktop/lengkubao.desktop/lengkubao.desktop.csproj`

授权签发工具（管理员用）：`tools/LicenseVerify/`。

## 环境要求

- Windows 10/11
- Visual Studio 2022（.NET 桌面开发、.NET Framework 4.7.2 开发包）
- 与手持机同一局域网；防火墙放行同步 TCP 及 UDP **8888**

## 构建

```bash
git clone <本仓库 URL>
cd lengkubao.desktop
```

1. 用 VS 打开 `lengkubao.desktop/lengkubao.desktop.sln`
2. 还原 NuGet（`packages.config`）
3. 配置 `Release|AnyCPU`，生成

或命令行（已安装 MSBuild / nuget）：

```bat
nuget restore lengkubao.desktop\lengkubao.desktop.sln
msbuild lengkubao.desktop\lengkubao.desktop.sln /p:Configuration=Release
```

输出一般在 `lengkubao.desktop/lengkubao.desktop/bin/Release/`。

首次启动会把旧安装目录下的 `data` 迁到用户数据区。日常读写主库，文档目录里的是备份副本。

## 数据路径

| 用途 | 路径 |
|------|------|
| 主库 | `%LOCALAPPDATA%\LengKuBao\data\lengkubao_{年}.db` |
| 自动备份 | `%USERPROFILE%\Documents\冷库宝自动备份\` |
| 授权文件 | `%ProgramData%\Lengkubao\license.lic` |

不要把程序装在会被「清理残留」扫掉的目录后，再把库放在安装目录里。当前设计已避开这一点。

## 与手持端配合

1. 启动电脑端，确认顶栏同步/连接为在线
2. 手持机扫顶栏二维码或输入配对码
3. 手持端做增量同步或全量拉取
4. 两端会计年度保持一致

## 上传 GitHub 前必须排除

**绝对不要提交：**

- `tools/private_key.xml`（授权私钥；公钥在程序里即可）
- `*.pfx` / ClickOnce 签名证书
- `license.lic`、客户 `.db`、备份目录、`bin/`、`obj/`、`packages/`
- 任何真实客户导出 Excel

`LicenseManager` 里的 **公钥** 可以随源码公开；**私钥** 只放在离线管理员机器，不要进 Git。

## 许可

私有业务软件。未声明开源协议前，禁止未授权复制与商用。
