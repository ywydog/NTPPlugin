<div align="center">

# NTP 时间同步服务端

[![GitHub](https://img.shields.io/badge/GitHub-%23121011.svg?logo=github&logoColor=white)](https://github.com/ClassIsland/NtpServer)

**为 ClassIsland 提供局域网 NTP 时间同步服务！**

> [!WARNING]
> - 此插件目前仅适用于 Windows 平台。
> - 使用标准 NTP 端口（UDP 123）需要以管理员身份运行 ClassIsland。

> [!NOTE]
> - 插件加载后自动启动 NTP 服务，无需手动操作。
> - 支持自定义监听端口，非特权端口（>1024）无需管理员权限。

## 功能特性

- **自动启动**：插件加载后自动启动 NTP 服务
- **可配置端口**：默认 UDP 123，可改为非特权端口
- **双时间源**：支持系统时间和 ClassIsland 精确时间
- **管理员权限检测**：非管理员时显示警告并提供一键重启到管理员模式
- **连接信息**：显示本机局域网 IP 地址，方便其他设备配置时间同步
- **请求统计**：实时显示已响应的 NTP 请求数

## 使用方式

1. 安装插件到 ClassIsland
2. 插件自动启动 NTP 服务
3. 在设置页面查看本机 IP 地址和端口
4. 在其他 ClassIsland 端的时间同步设置中填入 `IP:端口` 即可同步时间

## 设置页面

- **连接信息**：显示本机局域网 IP 地址列表（带复制按钮）和已响应请求数
- **NTP 服务设置**：
  - 监听端口（1-65535）
  - 时间源（系统时间 / ClassIsland 精确时间）
  - Stratum 级别（1-15）
- **服务控制**：手动重启/停止 NTP 服务

## 声明

- 该插件仅适用于 Windows。
- **这个插件适用于 ClassIsland 2.x（≥2.0.0.1）版本。**
- LGPLv3 许可。[LICENSE](./LICENSE)
