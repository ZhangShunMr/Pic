# Word终稿验收（待执行）

此项因当前环境无Microsoft Word而保留，不能由自动DOCX生成或非Word预览替代。

1. 先核对screenshots/windows-ui-evidence.json的源提交和全部检查结果，逐图查看真实WPF截图。程序若变更相关页面，先在Windows运行tools/verify-windows.ps1刷新证据和截图。
2. 使用python tools/build-materials.py生成操作说明书-草稿.docx。未提供专用模板时保持工作稿标记；如申请人另给模板，按实际模板核对标题、页眉、图注、尺寸与表格结构。
3. 用Microsoft Word打开DOCX并等待全部分页完成。逐页查看文字、图片、标题孤行、表格跨页、页眉页脚和空白页，图号及引用与正文一致，不能靠重复图或空白增加页数。
4. 在Word中读取实际页数，或由Word导出PDF后核对其页数。确认有效内容不少于10页；若不足，补充实际操作说明或有效截图，并重新在Word分页，不虚报数量。
5. 保存Word导出的终稿PDF和DOCX，记录Word版本、执行者、日期、逐页问题及修正、实际页数。更新materials-evidence.json中manualWordPageCount和状态，不能仅填一个未经执行的数字。
6. 将“草稿”改为“终稿”前，核对软件名称/版本/文件扩展名/默认值/来源/截图；填写申请人确认的身份、权属、开发完成和发表信息。未确认字段继续保留待核实。
