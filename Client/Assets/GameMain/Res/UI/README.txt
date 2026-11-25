蓝白科幻 UI 素材包
==================

参考方向：用户提供的两张游戏 UI 截图中的深蓝信息面板、青色操作按钮、黄色主操作与白色边线。
使用内置 image_gen 生成原创独立素材，没有提取参考截图中的角色、商标或文字。

目录与用途
----------
Common/Panels       通用信息面板、横向状态条
Common/Buttons      青色按钮、黄色主按钮、选中标签、圆形功能按钮
Common/Bars         进度条底槽、青色填充
Common/Icons        金币、钻石
Battle/Icons        能量、生命、攻击、防御、牌库
Collection/Frames   稀有卡牌框、物品槽框
MainMenu/Backgrounds 蓝白观景大厅背景

共 18 张 PNG：17 张带真实透明通道的部件，1 张不透明背景。
卡牌框和物品槽中央留空，可将插画或物品图标放在下层。
所有文字、费用、数量、星级和状态标签都应使用独立的 TextMeshPro/UI 对象。
本包是美术资源，不包含可交互窗口预制体、角色插画或业务逻辑。

Unity 导入与搭建
---------------
已附 Unity 2020 格式的 .meta：Sprite (2D and UI)、Single、Full Rect、Clamp、Bilinear、关闭 Mipmap、Alpha Is Transparency。
Pixels Per Unit 为 400；配合 Canvas 的 Reference Pixels Per Unit 100 使用。
图标最大导入尺寸 512，其余素材 2048；原始 PNG 不降采样、不修改像素。
面板、横条、横向按钮、选中标签和进度底槽设有初始九宫格边界，Image.Type 选 Sliced。
进度填充用 Image.Type = Filled / Horizontal / Left，fillAmount 设置进度。
圆形按钮、图标、卡框用 Simple；卡框、图标保持原比例，背景按画面需求等比覆盖。
进度条图像有上下透明留白，需要将底槽和填充各自的可见矩形对齐，而不是直接将两个 RectTransform 设为同样大小。
完整可见矩形、尺寸和推荐 Image 类型见项目根目录 Artifacts/UI/asset-catalog.json。
面板需要更明显的通透感时，可将 Image.color 的 Alpha 调至 0.78–0.9；面板上的文字保持不透明。
按钮可先用 Color Tint 实现状态：Normal 白色、Highlighted 轻微提亮、Pressed 约 0.85 亮度、Disabled 灰色并降低透明度。
九宫格边界为起始配置，实际窗口的最小尺寸与 Canvas 缩放比例应在本项目 Unity 内检查。

UGF / AssetBundle
-----------------
建议按 ui/common、ui/battle、ui/collection、ui/mainmenu 分包。
在 UGF Resource Editor 中明确登记需要运行时加载的 PNG，并保存资源集合。
共享按钮和面板只归入 ui/common；其他窗口通过依赖使用，不复制源文件。
未写入 Unity 的 AssetBundle 标签，避免与项目后续 UGF ResourceCollection 配置冲突。
README.txt、Tools/UIArt、Artifacts/UI 是说明、生成记录和预览，不纳入运行时资源包。

交付记录
--------
Tools/UIArt/asset-generation.json：18 张素材的最终提示词和原始生成文件位置。
Tools/UIArt/prepare-assets.cjs：原样复制素材、生成导入配置和检查透明通道。
Artifacts/UI/asset-catalog.json：尺寸、透明度统计、SHA256、分包建议。
Artifacts/UI/ui-preview.html：使用实际 PNG 组合的静态示例，可用浏览器打开。
Artifacts/UI/ui-preview.png：静态组合预览截图。

检查范围：PNG 格式、透明通道、卡框中央透明、文件复制一致性、导入配置与分类路径。
当前 MCP 连接的是另一 Unity 项目，所以没有操作该编辑器，也没有在此项目中验证 UI 运行效果。
