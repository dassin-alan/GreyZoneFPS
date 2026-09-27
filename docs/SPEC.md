# GreyZone FPS — 原型实现规格（SPEC v1，冻结）

> 本文件是两名实现者（Core lane / Unity lane）唯一的共同契约。
> **Core API 以 `Tools/CompileCheck/CoreApiStub.cs` 为唯一权威签名表**，本文件负责行为、数值与验收。
> 任何签名冲突以 stub 为准；任何行为冲突以本文件为准；两者都不明确时，选择更简单、更接近 CS 的实现。

---

## 0. 目标与验收

交付一个**可直接在 Unity 中按 Play 就玩**的轻量化手机风 3D FPS 原型（对标 2012 早期 CS:GO）：

- 4v4（可调 2–5）回合制爆破：冻结期购买 → 移动交战 → 安放/拆除炸弹 → 结算经济 → 下一回合。
- CS 式移动：加速/摩擦/急停、静步、蹲行、单段跳；**禁止**冲刺、滑铲、二段跳、技能/大招、自动回血。
- 武器：7 把（AK/M4/AWP/Deagle/P250/Glock/USP），弹道图案 + 扩散 + 后坐力恢复，数据驱动。
- 经济：起始 $800、上限 $16000、胜利/连败阶梯奖励、安放/拆除奖励、7 把枪价格。
- 性能取向：无实时光影、无 HDR/后处理、合并静态网格、对象池、零每帧 GC；手机触控 + 桌面键鼠两套输入。
- 调参：F1 面板可实时调 **急停相关全部参数** 与 **后坐力/扩散缩放**，并持久化。

**「可玩」的定义**：用一个干净场景（无任何手工摆放对象）启动，即可完成完整对局循环，且能通过下面的编译检查与 Core 测试。

---

## 1. 技术约束（硬性）

| 项 | 约定 |
|---|---|
| Unity | 2021.3+ / Unity 6 均可；**Built-in 渲染管线**（不用 URP/HDRP） |
| 依赖 | 只用内置模块（见 `Packages/manifest.json`）；**禁止任何外部包/资源商店资产** |
| 语言 | C# 7.3 语法上限（与 `-langversion:7.3` 编译检查一致） |
| 资产 | **全部程序化生成**：网格、材质、贴图、音效都在代码里生成；不新增二进制资产 |
| UI | **只用 IMGUI 绘制**（`GUI.*`），**禁止** uGUI/Canvas/TextMeshPro/EventSystem |
| 输入 | 旧版 `Input` API（`Input.touches` 多指、`Input.GetAxisRaw("Mouse X")`）；不用新 Input System |
| 动画 | 不用 Animator/SkinnedMeshRenderer；角色用分块网格 + 程序化位移 |
| 导航 | 不用 NavMesh；用地形路点图 + 转向 |
| 物理 | 只用 BoxCollider/CapsuleCollider/SphereCollider 查询；不用 Rigidbody、不用 MeshCollider |
| 光影 | 只有 1 盏无阴影方向光 + 环境光；**不允许任何实时阴影** |
| 禁区 | 不改 `Tools/CompileCheck/*`；不写 `README.md`；不执行 git 操作 |

---

## 2. 目录与所有权

```
D:\Projects\GreyZoneFPS\
├─ Assets\GreyZone\Scripts\Core\      ← Core lane 独占（纯 C#，无 UnityEngine）
├─ Assets\GreyZone\Scripts\Game\      ← Unity lane 独占（UnityEngine）
├─ Assets\GreyZone\Editor\            ← Unity lane 独占（UnityEditor，唯一允许处）
├─ Assets\GreyZone\Shaders\           ← Unity lane 独占（.shader）
├─ Tools\CoreTests\                   ← Core lane（控制台测试 + run_tests.ps1）
├─ Tools\CompileCheck\                ← 只读（已就绪：csc + 真实 Unity 参考程序集 + 脚本）
├─ docs\SPEC.md                       ← 本文件
```

**所有权纪律**：两个 lane 的写入路径完全不重叠，禁止跨目录修改。

---

## 3. Core lane（`GreyZone.Core`）

### 3.1 交付物

1. `Assets\GreyZone\Scripts\Core\*.cs`，`namespace GreyZone.Core`，**逐字实现 `CoreApiStub.cs` 的公开签名**（可增加 private/internal 成员，不可改公开签名）。
2. `Tools\CoreTests\Program.cs`：控制台断言harness（`static int Main()`，全部通过返回 0，失败返回 1，输出 `PASS <name>` / `FAIL <name> : 期望 vs 实际`）。
3. `Tools\CoreTests\run_tests.ps1`：自动找 csc（参考 `Tools\CompileCheck\check_all.ps1` 的查找逻辑），编译 `Core/*.cs` + `Program.cs` → `Tools\CoreTests\out\core_tests.exe`，编译成功则运行它并透传退出码。

### 3.2 移动实现（必须按此公式，Y 轴向上，单位米）

`MoveConfig.CreateDefault()` 默认值：

| 字段 | 值 | | 字段 | 值 |
|---|---|---|---|---|
| MaxSpeed | 6.35 | | JumpImpulse | 7.67 |
| Accel | 5.5 | | MaxSpeedCap | 8.128 |
| AirAccel | 12 | | WalkMultiplier | 0.52 |
| AirMaxWishSpeed | 0.762 | | DuckMultiplier | 0.34 |
| Friction | 5.2 | | StandHeight | 1.83 |
| StopSpeed | 2.032 | | DuckHeight | 1.37 |
| Gravity | 20.32 | | DuckTransitionTime | 0.20 |

`Step(state, cfg, input, weaponSpeedMultiplier, world, dt)` 顺序：

1. **蹲伏过渡**：`DuckFraction` 以 `1/DuckTransitionTime` 速率趋向 `DuckHeld?1:0`；反向起身时若 `world.OverlapCapsule(站立高度)` 为真则保持爬不起来（停留在当前高度）。`Ducked = DuckFraction > 0.5`。
2. **期望方向**：`wishDir = (sin(yaw),0,cos(yaw))*MoveForward + (cos(yaw),0,-sin(yaw))*MoveRight`；长度 >1 时归一化。
3. **摩擦（仅地面）**：`speed=|v.xz|`；若 `speed>1e-4`：`control=max(speed, StopSpeed)`；`newSpeed=max(0, speed - control*Friction*dt)`；按比例缩放 v.xz。
4. **加速**：地面 `accel=Accel, wishSpeed=min(|wishDir|*maxSpeed, maxSpeed)`；空中 `accel=AirAccel, wishSpeed=min(|wishDir|*maxSpeed, AirMaxWishSpeed)`。`current=dot(v.xz, wishDirNormalized)`；`add=wishSpeed-current`；若 `add>0`：`accelSpeed=min(accel*wishSpeed*dt, add)`，`v.xz += wishDirNormalized*accelSpeed`。
5. **重力**：空中 `v.y -= Gravity*dt`。
6. **跳跃（无二段跳）**：`JumpQueued && OnGround` → `v.y = JumpImpulse`，`OnGround=false`，清空队列。`MoveInput.JumpPressed` 入队，队列超过 `PlayerMover.JumpBufferTime` 失效。
7. **水平限速**：`|v.xz| > MaxSpeedCap` → 等比缩回。
8. **位移与碰撞**：用 `world.SweepCapsule(feet, radius, height, v*dt)` 推进，沿法线滑动（最多 4 次）；被挡住且在地面时尝试**台阶**（上移 `StepHeight` → 水平 → 下压）；用向下短扫确定 `OnGround`（距离阈值 0.06m）。
9. `ComputeMaxSpeed = MaxSpeed * weaponSpeedMultiplier * (walk?WalkMultiplier:1) * (duck?DuckMultiplier:1)`。
10. `CurrentHeight = lerp(StandHeight, DuckHeight, DuckFraction)`；眼睛高度 = `Position.y + CurrentHeight - 0.16`。

### 3.3 武器实现

`WeaponDef.All()` 返回 7 把（`Find(id)` 不区分大小写）：

| id | 名称 | Kind | 价格 | 伤害 | 穿透 | RPM | 弹匣/备弹 | 装填 | 伤害衰减 | 移速倍率 | 击杀奖励 | 站/移动/蹲/跳 扩散(°) | 开镜 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ak47 | AK-47 | Rifle | 2700 | 36 | 0.775 | 600 | 30/90 | 2.5 | 0.98 | 0.86 | 300 | 0.35/3.2/0.7/6.0 | — |
| m4a4 | M4A4 | Rifle | 3100 | 33 | 0.70 | 666 | 30/90 | 3.1 | 0.99 | 0.90 | 300 | 0.32/3.0/0.7/6.0 | — |
| awp | AWP | Sniper | 4750 | 115 | 0.975 | 41 | 10/30 | 3.7 | 0.99 | 0.80 | 100 | 0.10/6.0/0.5/10.0 | FOV 15 |
| deagle | 沙漠之鹰 | Pistol | 700 | 53 | 0.93 | 267 | 7/35 | 2.2 | 0.99 | 0.92 | 300 | 0.45/4.5/0.7/8.0 | — |
| p250 | P250 | Pistol | 300 | 38 | 0.64 | 400 | 13/26 | 2.2 | 0.95 | 0.96 | 300 | 0.50/4.0/0.7/7.0 | — |
| glock | Glock-18 | Pistol | 200 | 30 | 0.47 | 400 | 20/120 | 2.2 | 0.95 | 0.96 | 300 | 0.55/4.2/0.7/7.0 | — |
| usp | USP | Pistol | 200 | 35 | 0.50 | 352 | 12/24 | 2.2 | 0.95 | 0.96 | 300 | 0.50/4.0/0.7/7.0 | — |

（glock = T 默认手枪，usp = CT 默认手枪；两者都可单独购买。）

**后坐力**：`RecoilPitchScale/RecoilYawScale`：ak 1.0/1.0，m4a4 0.85/0.85，awp 1.6/1.2，deagle 1.3/1.2，p250 1.0/1.0，glock 0.7/0.7，usp 0.8/0.8。
`RecoilRecoveryDelay`：步枪 0.25，AWP 0.50，手枪 0.30。`RecoilRecoveryRate`：步枪 9，AWP 6，手枪 11（度/秒）。`RecoilResetTime`：步枪 0.6，AWP 1.0，手枪 0.5。`PatternVariance`：步枪 0.12，AWP 0.05，手枪 0.20。

**弹道图案**：数组为**逐发增量**（度），正 pitch = 视线上抬，正 yaw = 视线右偏。
AK-47（30 发，规范值，pitch/yaw）：

```
2.10 0.00 | 1.75 0.10 | 1.55 0.22 | 1.40 0.30 | 1.30 0.34
1.20 0.30 | 1.10 0.18 | 1.05 0.00 | 1.00 -0.22 | 0.95 -0.40
0.75 -0.52 | 0.60 -0.45 | 0.45 -0.28 | 0.35 -0.05 | 0.30 0.18
0.28 0.38 | 0.25 0.50 | 0.22 0.52 | 0.20 0.42 | 0.18 0.25
0.16 0.02 | 0.15 -0.20 | 0.14 -0.38 | 0.13 -0.50 | 0.12 -0.52
0.11 -0.42 | 0.10 -0.25 | 0.10 -0.05 | 0.09 0.15 | 0.08 0.30
```

- M4A4：按 AK 图案 ×0.85（pitch）与 ×0.85（yaw），前 3 发 pitch 再 ×0.9；自行微调后写入。
- 手枪（deagle/p250/glock/usp）：5 发短图案，首发抖动大、后续递减并左右交替（自行取值，保持“首发→次发”递减）。
- AWP：`[(3.6, 0.4), (3.4, -0.3)]`，单发循环。

**`TryFire`**：若 `Reloading` 先取消装填；校验 `CanFire`（`AmmoInMag>0 && now>=NextFireTime`）；`AmmoInMag--`；取 `idx = min(ShotIndex, pattern.Length-1)`；`pitch = pattern[idx].pitch * scale * (1 + (randPitch01*2-1)*PatternVariance)`（yaw 同理）；累加到 `st.RecoilPitch/RecoilYaw`；`ShotIndex++`；`NextFireTime = now + 60/Rpm`；`LastFireTime = now`。
**`Update`**：装填到点则补弹（`need=MagSize-AmmoInMag`，`take=min(need,ReserveAmmo)`）；`now-LastFireTime > RecoilRecoveryDelay` 时按 `RecoilRecoveryRate*dt` 把 RecoilPitch/Yaw 向 0 收敛；`now-LastFireTime > RecoilResetTime` 时 `ShotIndex=0`。
**`ComputeSpreadDeg`**：`s = SpreadStandDeg + SpreadMoveDeg*clamp01(speedRatio)`；空中 `s += SpreadJumpDeg`；蹲下 `s *= SpreadCrouchMultiplier`。

### 3.4 伤害与经济

- `HitGroupMultiplier`：Head 4.0 / Chest 1.0 / Stomach 1.25 / Arm 1.0 / Leg 0.75。
- `ComputeDamageAtDistance`：`round(Damage * RangeModifier^(dist/12.7))`，最小 1。
- `ApplyArmor`：有甲时 `health=round(damage*armorPen)`，`armorDamage=round((damage-health)*0.5)`，`armor=max(0,armor-armorDamage)`；无甲原样返回。
- `Economy.WinReward`：爆炸/拆除 3500，其余 3250。`LossReward(streak)`：`1400 + 500*(clamp(streak,1,5)-1)`（streak 含当前这一败）。`ClampMoney`：0–16000。`CanAfford`：`money>=price`。

### 3.5 Core 测试要求（`run_tests.ps1` 必须全绿）

1. 平地全速前进 2s：速度 ≈ `MaxSpeed*weaponMult`（±5%）。
2. 满速松开输入：0.5s 内速度 <0.1 m/s，滑行距离 <1.3m（**急停验收**；CS 摩擦模型下实测约 1.0m）。
3. 静步 ≈0.52×、蹲行 ≈0.34× 满速。
4. 跳跃最高点 1.2–1.7m；空中再按跳 **不产生** 向上速度（无二段跳）。
5. 全速持续前进 1s 后水平速度 ≤ `MaxSpeedCap`。
6. 0.3m 台阶可通过；0.6m 墙被挡住。
7. 斜向撞墙仍保留切向位移（能滑动）。
8. 武器：AK 连按两发间隔 ≥ `60/600`；30 发后弹匣为 0；备弹正确；装填后补满。
9. 后坐力：连射 10 发后 `RecoilPitch` 累计 >10°；停火 2s 后收敛到 ≈0；`ShotIndex` 归零。
10. 扩散：静止 0.35°，满速时 ≈0.35+3.2，空中额外 +6，蹲下 ×0.7（AK）。
11. 伤害：AK 对躯干无甲 36；对 100 甲≈28 生命 + 约 4 甲损；AWP 躯干 ≥100。
12. 经济：`LossReward(1)=1400`、`(5)=3400`、`(9)=3400`；`ClampMoney(20000)=16000`。

---

## 4. Unity lane（`GreyZone.Game` / `GreyZone.EditorTools`）

### 4.1 启动（必须零场景配置）

- `GameBootstrap`：`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]` 创建根对象 `GreyZone` + `GameDirector`，**在没有手工配置的空场景里也能完整运行**；场景里若已存在 `Camera`/`Light` 就复用，否则自建。
- 渲染/质量（启动时设置）：`Application.targetFrameRate = 60`；`QualitySettings`：阴影 `Disable`、`shadowDistance=0`、`pixelLightCount=1`、`antiAliasing=0`、`vSyncCount=0`、`skinWeights=OneBone`；`RenderSettings.fog=true, Linear, 25→140, #6A6F73`；环境光 Flat 深灰；相机：`clearFlags=SolidColor`（灰 #7E858A）、`farClipPlane=200`、`fov=70`、无 HDR、无后处理、无 skybox。
- `Physics.autoSimulation = false`（只做查询，不跑刚体）；`Physics.queriesHitTriggers = true`（自行过滤，见 4.3）。

### 4.2 地图（原创灰盒 `de_compound`）

约 64m × 44m，外墙 4m 高：T 出生点南侧、CT 出生点北侧；**A 点**＝东侧仓库（室内 + 木箱掩护），**B 点**＝西侧货场（集装箱/板条箱）；中路走廊连接，另有东侧长道与西侧暗道人两条侧路。
- 掩护箱尺寸 0.9/1.2/1.8m，可叠放；立柱若干；**禁止斜坡**，高差一律用 ≤0.4m 台阶。
- 顶点色（低饱和写实）：地面 `#6B6E6A`、墙 `#7A7D78`、木箱 `#6E5A3F`、钢件 `#5C5F5A`、暗部 `#4E514C`、炸弹点标记暗红 `#7A3B36`。
- 视觉网格**合并为 ≤12 个 chunk**（每 chunk 一个 MeshFilter/MeshRenderer，共用 1 个材质）；碰撞体用独立 BoxCollider（≤140 个，不做子物体层级）。
- 输出：双方各 5 个出生点、A/B 点触发区域、**40–60 个路点（带邻接表）**、若干 bot 站位点。

### 4.3 角色、碰撞与命中

- 每名角色：`CapsuleCollider`（r=0.4，高度随蹲伏更新）挂 `ActorBody` 标记；三个**非 Trigger** 命中盒挂 `BotHitbox`（枚举 `HitGroup`）：头（球 r=0.13，高约 1.65m）、胸腹（0.5×0.6×0.35，中心约 1.3m）、腿（0.45×0.85×0.3，中心约 0.45m）。
- 移动扫掠过滤：忽略 `isTrigger` 的碰撞体（命中盒/炸弹点机关都是 Trigger），保留地形盒与角色胶囊 → 角色互相阻挡。
- 子弹射线：用 `Physics.RaycastNonAlloc` + 预分配数组；取最近命中，**跳过 `ActorBody`**（让内部命中盒继续参与），忽略 Trigger 但 `BotHitbox` 除外；无命中盒命中则判定为墙（弹孔 + 火花）。
- 玩家与 bot 均使用 Core `PlayerMover`，固定步长 **1/64s** 累积推进；相机/表现层与物理步解耦。

### 4.4 玩家

- 相机：yaw/pitch 由输入驱动，pitch 限 ±89°；**后坐力偏移直接叠加到视线**（`yaw += st.RecoilYaw`，`pitch += st.RecoilPitch`，让玩家自然压枪）。
- 冻结期禁止移动/开火/使用。
- 开火：从相机中心按 `ComputeSpreadDeg` 采样圆锥（`Random.insideUnitCircle`）+ 当前后坐力偏移决定弹道；射程 200m；友军不受伤（仍出弹孔）。
- AWP/RMB（触控为 SCOPE 键）：FOV 15 + 开镜遮罩（IMGUI 黑边 + 十字线），灵敏度 ×0.4；移动/跳跃时扩散惩罚照常。
- 换弹 R、切枪 1/2（触控 SWAP 循环）、使用键 E（长按安放/拆除，显示进度条）。
- 死亡：镜头留在原地可自由观察 + “已阵亡”提示；回合结束自动复活。

### 4.5 机器人

- 数量：`EnemyCount` 默认 4（可 2–5），我方人数 = EnemyCount-1 + 玩家；玩家阵营默认 T（设置可切 CT），bot 在对面。
- 状态机：`巡逻（按角色路线点）→ 调查（听到枪声/队友阵亡 30m 内）→ 交战（有视线）→ 安放/拆除`；记忆最后目击点 5s。
- 感知：FOV 120°、视线射线节流（每 bot 0.15s 错峰，`RaycastNonAlloc`）。
- 战斗：反应延迟 + 瞄准误差锥随时间收敛；3–6 发点射；**停下再打**；弹匣空自动换弹；难度预设 Easy/Normal/Hard（反应 0.45/0.30/0.18s，误差 5°/3°/1.6°）。
- 经济简化：按回合数给装备档（R1 手枪；R2–3 手枪+半甲 或 SMG；R4+ 步枪+甲/头盔；R4 起每 5 回合一名敌方 AWP）。
- 外观：两段合并网格（下半身腿 / 上半身+头+手+枪）+ 顶点色；上半身朝向瞄准方向；死亡 = 整体倒下 90° 后 3s 消失；无 ragdoll、无 Animator。
- 子弹同样走 Core 伤害/护甲公式；击杀者获得 `KillReward`（玩家计钱，bot 不计）。

### 4.6 回合流程与经济（`GameDirector`）

冻结 10s（可购买，能显示倒计时、不能移动）→ 进行 115s（购买窗口再开 20s）→ 结束（炸弹爆炸 / 拆除 / 团灭 / 超时）→ 结算 5s → 下一回合。
- 埋包后 T 全灭不结束，继续等引爆/拆除；超时且未埋包 = CT 胜。
- 玩家存活保留武器与剩余护甲；死亡则下回合回默认手枪、护甲清零。
- 结算：胜方按 `Economy.WinReward`，败方按连败阶梯（`LossStreak` 各自维护）；埋包/拆包即时给个人与团队奖励。
- 半场：每 8 回合交换阵营；先到 8 胜结束比赛，之后可重开。
- 购买：7 把枪 + 护甲 $650 + 头盔 $350（与护甲同买共 $1000）+ 拆弹器 $400（仅 CT）；价格取自 `WeaponDef.Price` 与 `Economy`。

### 4.7 HUD（IMGUI，字符串/样式缓存，数值变化才更新）

准星（间距随 `ComputeSpreadDeg` 实时变化）、命中标记、HP/护甲、弹匣/备弹、金钱（绿）、回合计时 + 炸弹倒计时（红闪）、比分 T:CT、击杀信息（≤5 条 / 5s）、回合结算横幅、冻结倒计时、安放/拆除进度条、雷达（左上 96px：队友、2s 内可见的敌人、炸弹、A/B 点）。
购买菜单：购买窗口内按键或点击购买，买不到变灰，窗口结束自动关闭（桌面版打开时解锁鼠标）。按 Esc/移动端 PAUSE 打开暂停面板。

### 4.8 触控（`Application.isMobilePlatform` 或设置强制开启）

- 多指自研：`Input.touchCount` + `Input.GetTouch`，IMGUI 只负责画，命中判定用归一化矩形。
- 左摇杆：屏幕左下 45%×50% 区域，底盘半径 90px（1080p 基准），死区 0.12，输出 `MoveForward/MoveRight`。
- 右半屏拖动 = 视角（灵敏度可调、按倍镜缩放）。
- 按钮：FIRE（右下大）、SCOPE、JUMP、CROUCH、WALK（切换）、RELOAD、USE（上下文：安放/拆除）、BUY、SWAP、PAUSE（左上）；支持左手镜像。
- 辅助（设置项）：**开火自动急停**（默认开：按下开火时用 `Friction×3.5`、`StopSpeed×2` 的 `MoveConfig` 副本推进 0.25s）；**瞄准辅助 0–100%（默认 30%）**：仅对 ±6° 内目标做灵敏度减速（最多 ×0.65），绝不自动拉枪。

### 4.9 特效与音频（全部池化）

曳光（32）、弹着火花（48）、弹孔贴片（64，10s 淡出）、枪口火光（8）、血迹（16）、爆炸、每角色 1 个贴地假阴影。
音效程序化生成（`AudioClip.Create` + `SetData`）：每把枪的枪声（噪声爆发+衰减，按威力区分）、换弹咔嗒、命中标记、脚步、炸弹滴答（频率加快）、安放/拆除提示、爆炸、回合胜负短音、死亡闷响。3D 空间音 + 线性衰减（3→60m），并发 voice ≤24（超出杀最旧），Listener 在相机上。

### 4.10 性能规则（必须遵守）

- 每帧零 GC：缓存字符串/GUIStyle/数组，禁止 LINQ、禁止 `foreach` 字典、禁止 Update 内 `new`；对象池覆盖所有特效与音源；`Instantiate` 只允许在回合开始/重开时。
- 枪口火光、曳光、贴片一律无光照、无阴影、无实时灯。
- 典型交火画面 DrawCall ≤120；静态地图 ≤12 个 chunk renderer；角色 ≤2 renderer/人。
- 所有物理查询用 NonAlloc 版本；bot 感知错峰。
- 画质档位（低/中/高）：雾距与 `farClipPlane`（80/150/200）、特效池上限、`targetFrameRate`（30/60/60）。

### 4.11 调参面板（F1，硬性要求）

滑杆：`Accel / Friction / StopSpeed / JumpImpulse / Gravity / AirAccel / AirMaxWishSpeed / WalkMultiplier / DuckMultiplier / MaxSpeed / RecoilScale(0–2) / SpreadScale(0–2) / 鼠标灵敏度 / 触控灵敏度 / FOV / 瞄准辅助%`；开关：**开火自动急停**；下拉：Bot 难度、画质档、我的阵营；数值：敌人数（下一回合生效）；按钮：重开比赛、恢复默认。
修改立即作用到 `MoveConfig` / 开火路径（`RecoilScale` 乘 kick、`SpreadScale` 乘扩散），并写入 PlayerPrefs（前缀 `gz.`）。

### 4.12 编辑器菜单（`Assets\GreyZone\Editor`，唯一允许用 UnityEditor 的地方）

- `GreyZone/创建场景并加入构建列表`：新建空场景存为 `Assets/GreyZone/Scenes/GreyZone.unity`，写入 `EditorBuildSettings.scenes`，`AssetDatabase.Refresh()`。
- `GreyZone/应用移动端设置`：Android = IL2CPP + ARM64 + minSdk 24 + Vulkan&GLES3、横屏、产品名/bundle id。
- 编辑器加载时若场景文件缺失，用 `EditorApplication.delayCall` 也自动执行一次「创建场景」（避免首次构建无场景）。
- 全部包 `try/catch` + `Debug.Log/LogError`；只用最稳定的 UnityEditor API。

---

## 5. 桌面操作

WASD 移动、鼠标视角（锁定光标）、左键开火、右键开镜、R 换弹、1/2 切枪、Shift 静步、Ctrl 蹲、Space 跳、E 安放/拆除、B 购买菜单、F1 调参、Esc 暂停/解锁光标。

---

## 6. 验证与自检（两个 lane 都必须给出命令与真实输出）

| lane | 命令 | 通过标准 |
|---|---|---|
| Core | `powershell -ExecutionPolicy Bypass -File Tools\CoreTests\run_tests.ps1` | 全部 `PASS`，退出码 0 |
| 两个 lane | `powershell -ExecutionPolicy Bypass -File Tools\CompileCheck\check_all.ps1` | `PASS: compiled clean` |

`check_all.ps1` 已就绪：自动找 Roslyn csc；引用 `Tools\CompileCheck\refs`（真实 Unity 2021.3 参考程序集）；Core 目录为空时自动用 `CoreApiStub.cs` 占位，所以 **Unity lane 可以在 Core 完成前独立编译自检**。

---

## 7. 报告格式（lane 完成时必须给出）

1. 新增/修改文件清单（全路径）。
2. 实际执行的命令 + 关键输出粘贴（编译/测试）。
3. 与 SPEC 的偏差（没有就写“无”）。
4. 已知风险 / 未能验证的部分。
5. 依赖 Core API 的调用点清单（Unity lane 必填，便于集成核对）。

---

## 8. 数值扩展点（后续版本，不在本轮范围）

手雷/闪光/烟雾/燃烧瓶、武器掉落拾取、观战队友、记分板、排位与匹配、网络联机、真实 lightmap 烘焙管线与 URP 迁移。
