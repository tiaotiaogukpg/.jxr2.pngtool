# JXR2PNG

将 GameBar 截图的 `.jxr` 转为 sRGB `.png`。转换成功后删除原 JXR。

---

## 文件结构

| 文件 | 用途 |
|------|------|
| **Program.cs** | 主程序源码 |
| **JXR2PNG.csproj** | 项目配置 |
| **JXR2PNG.bat** | 运行器：优先 exe，否则调用 ps1 |
| **JXR2PNG.ps1** | 脚本备选（需 PowerShell） |
| **README.md** | 说明 |

---

## 使用

1. 将 `JXR2PNG.exe` 复制到 GameBar 捕获目录（如 `Videos\Captures\`）
2. 双击运行，自动转换该目录下所有 `.jxr`

---

## exe 与 bat+ps1 的区别

| 项目 | **JXR2PNG.exe** | **JXR2PNG.bat + JXR2PNG.ps1** |
|------|-----------------|-------------------------------|
| **依赖** | 无，单文件自带 .NET | 需系统有 PowerShell |
| **调用关系** | 独立运行 | bat 优先调 exe，无 exe 时逐个调用 ps1 |
| **解码/编码** | WinRT（内存流）优先，失败回退 WIC（COM） | 纯 WinRT（StorageFile） |
| **色彩处理** | WIC 色彩管理 或 WinRT 直接输出 | WinRT 输出后，用 System.Drawing 做线性→sRGB gamma 校正 |
| **HDR 支持** | 支持，非 Bgra8 自动转为 Bgra8 | 直接 SetSoftwareBitmap，HDR 格式可能失败 |
| **中文路径** | 用内存流，避免路径问题 | StorageFile 对部分中文路径可能异常 |
| **输出** | 控制台显示扫描目录、每文件结果、成功/失败统计 | bat 静默调用 ps1，仅显示成功/失败 |
