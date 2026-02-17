# JXR2PNG 工具

将 GameBar 截图生成的 `.jxr` 转为标准 sRGB `.png`，解决原 PNG 偏暗问题；转换后覆盖同名 PNG 并删除原 JXR。

---

## 文件一览

| 文件 | 用途 |
|------|------|
| **JXR2PNG.exe** | 主程序（编译产物），C# 控制台，纯 WIC 色彩管理，速度最快 |
| **JXR2PNG.bat** | 批处理入口：扫描当前目录 `.jxr`，逐个转换，成功后删除原文件 |
| **JXR2PNG.ps1** | 脚本版实现（备选）：PowerShell + WIC + System.Drawing gamma 校正 |
| **Program.cs** | C# 主程序源码 |
| **JXR2PNG.csproj** | C# 项目配置（.NET 10） |
| **build.bat** | 编译脚本：`dotnet build` 及单 exe 发布 |
| **.gitignore** | Git 忽略规则 |
| **README.md** | 本说明文档 |

---

## 各文件详细说明

### JXR2PNG.exe（推荐使用）

- **作用**：将单个 JXR 转为 PNG  
- **用法**：`JXR2PNG.exe input.jxr output.png`  
- **实现**：纯 WIC（无 System.Drawing / LockBits / 像素循环）
  - `IWICBitmapDecoder` 解码 JXR
  - `IWICColorTransform` 做线性→sRGB 色彩转换
  - `IWICBitmapEncoder` 输出 PNG
- **输出**：含 sRGB 色彩信息（iCCP / gAMA / cHRM）的标准 PNG  
- **位置**：编译后在 `bin\Release\net10.0\JXR2PNG.exe`；单 exe 发布后在 `bin\Release\net10.0\win-x64\publish\JXR2PNG.exe`

### JXR2PNG.bat

- **作用**：批量转换当前目录下所有 `.jxr`
- **流程**：
  1. 切换到脚本所在目录
  2. 扫描 `*.jxr`
  3. 对每个 JXR 调用 `JXR2PNG.ps1` 生成同名 `.png`
  4. 生成成功后删除对应 `.jxr`
  5. 输出进度
- **依赖**：`JXR2PNG.ps1`、PowerShell  
- **编码**：GBK，编辑后若乱码请以 ANSI/GBK 保存

### JXR2PNG.ps1

- **作用**：脚本版 JXR→PNG 转换（备选实现）
- **实现**：WIC 解码 + 编码，再通过 System.Drawing + LockBits + gamma LUT 做线性→sRGB 校正
- **用法**：`.\JXR2PNG.ps1 <file.jxr> [file2.jxr ...]` 或由 `JXR2PNG.bat` 调用
- **依赖**：Windows 自带 PowerShell、WIC（WMPhoto 解码器）

### Program.cs

- **作用**：JXR2PNG.exe 的 C# 源码
- **结构**：COM 初始化 → 解码 → 色彩转换 → 编码 → COM 释放
- **无**：System.Drawing、LockBits、托管像素循环、手写 gamma

### JXR2PNG.csproj

- **作用**：.NET 项目配置
- **目标框架**：net10.0  
- **输出**：控制台 exe

### build.bat

- **作用**：一键编译 JXR2PNG.exe
- **步骤**：`dotnet build` 后执行 `dotnet publish` 生成单 exe
- **依赖**：.NET SDK；若 `dotnet` 不在 PATH，会尝试 `C:\Program Files\dotnet\dotnet.exe`
- **用法**：双击或命令行执行

### .gitignore

- **作用**：指定 Git 忽略的文件（IDE、缓存、临时文件等）

---

## 编译与发布

```bash
# 普通编译
dotnet build -c Release

# 单 exe 发布
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

或直接运行 `build.bat`。

---

## 部署与使用

1. 将以下文件复制到 GameBar 捕获目录（如 `C:\Users\<用户名>\Videos\Captures\`）：
   - `JXR2PNG.exe`（或 `JXR2PNG.ps1` + `JXR2PNG.bat`）
2. 双击 `JXR2PNG.bat` 批量处理；或命令行执行：
   ```
   JXR2PNG.exe input.jxr output.png
   ```
3. 脚本会覆盖与 JXR 同名的 PNG，并删除原 JXR。

---

## 说明

- 依赖 Windows 内置 WIC（WMPhoto 支持 JXR）
- 仅覆盖与 `.jxr` 同名的 `.png`，不删除其他图片
- 若使用 exe：需将 `JXR2PNG.bat` 改为调用 exe 而非 ps1
