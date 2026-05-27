# PdfOutlineFonts

一个基于 C# WPF + Ghostscript.NET 的 PDF 批量转曲小工具，面向印刷行业场景。

![screenshot](screenshot.png)

## 功能

- 批量添加 PDF（按钮多选 + 拖拽）
- DataGrid 展示文件名、路径、状态、进度
- 使用 Ghostscript `pdfwrite` + `-dNoOutputFonts` 转曲输出矢量 PDF
- 支持设置输出目录（默认源文件同目录，输出后缀 `_outlined`）
- 支持并发转换（最多 2 个）
- 支持取消转换
- 单文件失败不影响整体任务
- 完成后显示成功/失败汇总

## 使用说明

1. 从 Ghostscript 官网下载 64 位 `gsdll64.dll`：<https://www.ghostscript.com/>
2. 将 `gsdll64.dll` 放到 `src/PdfOutlineFonts/Assets/` 目录，并设置为 `Embedded Resource`
3. 发布后直接运行 exe，无需额外安装依赖

## 首次运行说明

程序会在启动时将嵌入资源 `gsdll64.dll` 释放到：

`%TEMP%/PdfOutlineFonts/gsdll64.dll`

如果释放或加载失败，界面底部会显示明确错误信息。

## 开源协议

本项目以 [GPL v3](LICENSE) 协议开源。

由于项目可捆绑 Ghostscript 原生库（`gsdll64.dll`）一同分发，因此遵循 GPL 兼容分发方式。

## Ghostscript 版权声明

Ghostscript is copyright © Artifex Software, Inc.
Ghostscript is distributed under GNU AGPL/GPL and commercial licenses by Artifex.
