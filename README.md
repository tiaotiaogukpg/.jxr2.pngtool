# .jxr2.pngtool

用来对付 GameBar 截图同时产生 `.png` 和 `.jxr`，且 `.png` 偏暗的问题。将 `.jxr` 转为 `.png`（覆盖偏暗的 png），并删除原 `.jxr`。

---

## 文件说明

### JXR2PNG.bat

批处理入口，负责：

1. 切换到脚本所在目录（即 GameBar 捕获目录）
2. 扫描当前目录下所有 `.jxr`
3. 逐个调用 `JXR2PNG.ps1` 进行转换
4. 转换成功后删除对应 `.jxr`
5. 输出处理进度与结果

### JXR2PNG.ps1

PowerShell 脚本，负责：

1. 接收一个或多个 `.jxr` 文件路径
2. 使用 Windows 内置 WIC（WMPhoto 解码器）解码 JXR
3. 在同目录下生成同名 `.png`，覆盖已有同名文件
4. 对生成的 `.png` 应用亮度提升（默认 1.1 倍）
5. 输出生成的 `.png` 完整路径

如需调整亮度，可修改脚本中的 `$BrightnessFactor`（>1 变亮，<1 变暗）。

---

## 部署

将 `JXR2PNG.bat` 和 `JXR2PNG.ps1` 复制到 GameBar 捕获目录，例如：

```
C:\Users\<用户名>\Videos\Captures\
```

无需安装 Python 或第三方软件，仅需 Windows 自带 PowerShell。

---

## 使用

双击 `JXR2PNG.bat` 即可。脚本会依次处理目录下所有 `.jxr`，生成 `.png` 后自动删除 `.jxr`。

---

## 说明

- 仅 2 个文本文件，约 4KB
- 依赖 Windows 内置 WIC（WMPhoto 解码器）
- 仅覆盖与 `.jxr` 同名的 `.png`，不删除其他图片
