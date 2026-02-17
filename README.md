# JXR2PNG 工具

将 GameBar 截图生成的 `.jxr` 转为标准 sRGB `.png`，解决原 PNG 偏暗问题。转换成功后删除原 JXR。

---

## 文件一览

| 文件 | 用途 |
|------|------|
| **JXR2PNG.exe** | 主程序（编译产物），双击即批量转换 exe 所在目录下的 .jxr |
| **JXR2PNG.bat** | 批处理：优先调用同目录 exe，否则调用 ps1 |
| **JXR2PNG.ps1** | 脚本版（备选）：PowerShell + WIC + gamma 校正 |
| **Program.cs** | C# 主程序源码 |
| **JXR2PNG.csproj** | .NET 10 项目配置 |
| **build.bat** | 编译并发布单 exe |
| **.gitignore** | Git 忽略规则 |
| **README.md** | 本说明 |

---

## 使用

### 推荐：JXR2PNG.exe

1. 将 `JXR2PNG.exe` 复制到 GameBar 捕获目录（如 `C:\Users\<用户名>\Videos\Captures\`）
2. 双击运行
3. 程序会扫描 exe 所在目录内所有 `*.jxr`、`*.JXR`，逐个转为同名 `.png`，成功后删除原文件
4. 结束时显示成功/失败数量，按 Enter 退出

无需参数，双击即可批量转换。

### 备选：JXR2PNG.bat + JXR2PNG.ps1

当 exe 不存在时，`JXR2PNG.bat` 会调用 `JXR2PNG.ps1` 做转换，依赖 PowerShell 和 System.Drawing。

---

## 各文件说明

### JXR2PNG.exe

- **运行方式**：双击，无命令行参数
- **扫描目录**：exe 所在目录（使用 `Environment.ProcessPath`，单文件 exe 下正确）
- **匹配文件**：`*.jxr`、`*.JXR`
- **输出**：同名 `.png`，含 sRGB 色彩信息（iCCP/gAMA/cHRM）
- **删除**：仅当 PNG 成功生成后才删除原 `.jxr`
- **实现**：纯 WIC（IWICBitmapDecoder → IWICColorTransform → IWICBitmapEncoder），无 System.Drawing、LockBits、像素循环

### JXR2PNG.bat

- 优先查找同目录 `JXR2PNG.exe`，存在则直接运行
- 无 exe 时调用 `JXR2PNG.ps1` 批量转换
- 编码：GBK，编辑后若乱码请以 ANSI/GBK 保存

### JXR2PNG.ps1

- 脚本版转换，依赖 WIC + System.Drawing gamma 校正
- 用法：`.\JXR2PNG.ps1 <file.jxr> [file2.jxr ...]` 或由 bat 调用

### Program.cs

- Main 为无参数批量模式
- 流程：COM 初始化 → 扫描 → 逐个 ConvertJxrToPng → 删除成功项 → 统计 → 等待 Enter

### JXR2PNG.csproj

- 目标：net10.0，OutputType=Exe
- 单 exe 发布：`dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`

### build.bat

- 执行 `dotnet build` 和 `dotnet publish`
- dotnet 不在 PATH 时尝试 `C:\Program Files\dotnet\dotnet.exe`
- 普通输出：`bin\Release\net10.0\JXR2PNG.exe`
- 单 exe 输出：`bin\Release\net10.0\win-x64\publish\JXR2PNG.exe`

---

## 编译与发布

```bash
# 普通编译
dotnet build -c Release

# 单 exe 发布（推荐，双击即用）
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

或直接运行 `build.bat`。

---

## 部署

将 `bin\Release\net10.0\win-x64\publish\JXR2PNG.exe` 复制到 GameBar 捕获目录，双击即可批量转换该目录下所有 `.jxr`。

---

## 说明

- 依赖 Windows 内置 WIC（WMPhoto 支持 JXR）
- 仅处理 `.jxr` 文件，仅删除转换成功后的原 jxr
- 单 exe 为自包含，无需安装 .NET 运行时
