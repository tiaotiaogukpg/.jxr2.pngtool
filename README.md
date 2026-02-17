# JXR2PNG

将 GameBar 截图的 `.jxr` 转为 sRGB `.png`。转换成功后删除原 JXR。

---

## 文件结构

| 文件 | 用途 |
|------|------|
| **Program.cs** | 主程序源码 |
| **JXR2PNG.csproj** | 项目配置 |
| **publish.bat** | 编译并输出单 exe 到 `publish\` |
| **JXR2PNG.bat** | 运行器：优先 exe，否则调用 ps1 |
| **JXR2PNG.ps1** | 脚本备选（需 PowerShell） |
| **README.md** | 说明 |

---

## 使用

1. 运行 `publish.bat` 生成 `publish\JXR2PNG.exe`
2. 将 `JXR2PNG.exe` 复制到 GameBar 捕获目录（如 `Videos\Captures\`）
3. 双击运行，自动转换该目录下所有 `.jxr`

---

## 编译

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

或直接运行 `publish.bat`，输出到 `publish\JXR2PNG.exe`（约 11MB，无需安装 .NET）。
