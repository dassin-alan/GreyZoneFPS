# GreyZone — 轻量化手机 3D FPS 原型（对标 2012 早期 CS:GO）

一个**打开 Unity 按 Play 就能玩**的回合制爆破 FPS 原型：4v4（可调）、买枪经济、CS 式急停与弹道、机器人、触控 + 键鼠双操作。
全部资产（地图网格、角色、贴图、音效、UI）都在运行时程序化生成——**没有任何二进制资源、没有任何外部包**。

---

## 快速开始

1. Unity Hub → **Add project from disk** → 选择本目录（`GreyZoneFPS`）。
2. 用 **Unity 2021.3 LTS 或更新（含 Unity 6）** 打开（提示升级确认即可；项目使用 Built-in 渲染管线）。
3. 打开后等待首次导入完成，直接 **按 Play** 即可游玩（无需任何场景配置）。
4. 想构建 Android APK：
   - 菜单 `GreyZone → 创建场景并加入构建列表`（首次打开通常已自动执行）
   - 菜单 `GreyZone → 应用移动端设置`（IL2CPP / ARM64 / Vulkan+GLES3 / 横屏）
   - `File → Build Settings → Android → Build`

> 首次打开时若没有场景，编辑器会自动创建 `Assets/GreyZone/Scenes/GreyZone.unity` 并加入构建列表；也可以手动用上面的菜单执行。

## 操作

**桌面（键鼠）**

| 操作 | 按键 |
|---|---|
| 移动 | W A S D |
| 视角 | 鼠标 |
| 开火 / 开镜 | 左键 / 右键（AWP） |
| 跳 / 蹲 / 静步 | Space / Ctrl / Shift |
| 换弹 / 切枪 | R / 1、2 |
| 安放·拆除 | E（长按） |
| 购买菜单 | B（冻结期与回合前 20 秒） |
| 调参面板 | F1 |
| 暂停 / 释放鼠标 | Esc |

**触控（手机，或设置里强制开启）**：左半屏摇杆移动；右半屏拖拽瞄准；右下 FIRE；另有 SCOPE / JUMP / CROUCH / WALK / RELOAD / USE / BUY / SWAP / PAUSE 按钮；支持左手镜像。
辅助项（设置/F1 面板）：**开火自动急停**（默认开）、**瞄准辅助 0–100%**（默认 30%，只做灵敏度减速，不自动拉枪）。

## 可调手感（核心要求）

按 **F1** 打开调参面板，实时生效并持久化（PlayerPrefs，前缀 `gz.`）：

- **急停/移动**：Accel、Friction、StopSpeed、JumpImpulse、Gravity、AirAccel、AirMaxWishSpeed、Walk/Duck 倍率、MaxSpeed。
- **后坐力/精度**：RecoilScale（0–2）、SpreadScale（0–2）。
- 其它：鼠标/触控灵敏度、FOV、瞄准辅助、Bot 难度、画质档、阵营、敌人数、重开比赛、恢复默认。

## 玩法（对标 2012 早期 CS:GO）

- 回合制爆破：冻结 10s（可购买）→ 进行 115s → 结算 5s；埋包 3s、炸弹 40s、拆包 10s（带拆弹器 5s）。
- 经济：起始 $800、上限 $16000；胜方 $3250/$3500；败方连败阶梯 $1400→$3400；埋包/拆包奖励。
- 武器：AK-47 $2700 / M4A4 $3100 / AWP $4750 / 沙漠之鹰 $700 / P250 $300 / Glock-18 $200 / USP $200 + 护甲 $650 / 头盔 +$350 / 拆弹器 $400（CT）。
- 数值：命中头 ×4、腹 ×1.25、腿 ×0.75；CS 护甲公式；移动/跳跃射击精度惩罚。
- **没有**：冲刺、滑铲、二段跳、技能/大招、自动回血、步枪 ADS、皮肤。
- 比赛：先到 8 胜；第 8 回合半场交换阵营。

## 性能设计（移动端优先）

- 全部烘焙式观感：无实时阴影、无 HDR、无后处理；1 盏无阴影方向光 + 环境光 + 线性雾。
- 静态地图合并为 ≤12 个 chunk 网格共用 1 个材质；顶点色 + 灰度纹理；全部对象池；物理查询 NonAlloc；目标每帧零 GC。
- 画质档（低/中/高）调整视距、雾距与帧率上限（30/60）。

## 自检与验证（不需要打开 Unity）

```powershell
# Core 仿真层单元测试（移动/急停/后坐力/经济/伤害）
powershell -ExecutionPolicy Bypass -File Tools\CoreTests\run_tests.ps1

# 全部脚本编译检查（真实 Unity 2021.3 参考程序集 + Roslyn）
powershell -ExecutionPolicy Bypass -File Tools\CompileCheck\check_all.ps1
```

`Tools\CompileCheck\refs\` 里的参考程序集来自 NuGet 包 `UnityEngine.Modules`（Unity 官方发布），仅用于离线编译检查。
删掉该目录后脚本也会自动尝试从本机 Unity 安装目录查找。

## 目录结构

```
Assets/GreyZone/Scripts/Core/   纯 C# 仿真层（移动/弹道/经济）——无 UnityEngine 依赖，可单测
Assets/GreyZone/Scripts/Game/   Unity 表现层（地图/玩家/Bot/回合/HUD/触控/特效/音效）
Assets/GreyZone/Shaders/        自写轻量 shader（顶点色光照/透明/叠加）
Assets/GreyZone/Editor/         编辑器菜单（建场景/构建列表/移动端设置）
Tools/CoreTests/                Core 控制台测试
Tools/CompileCheck/             离线编译检查
docs/SPEC.md                    实现规格（数值与行为契约）
```

## 已知边界（原型范围外）

- 单机 vs Bot（无联机）；无手雷/闪光/烟雾/燃烧瓶；无武器掉落拾取；无观战队友；无记分板。
- 无真实 lightmap 烘焙管线（原型用顶点色 + 环境光模拟烘焙观感）；正式资产管线见 `docs/SPEC.md` 与立项文档。
- 美术为灰盒占位（程序化几何/灰度纹理），配色与氛围按早期 CS:GO 的灰暗硬朗取向。

## 合规

项目为原创技术原型：不包含、不复制 Valve 的任何资产、地图、UI 图形或名称；武器名称为现实通用枪械型号描述。项目代号 GreyZone 仅作内部占位。
