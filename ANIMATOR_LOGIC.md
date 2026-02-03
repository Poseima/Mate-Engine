# Mate Engine Animator 系统完整文档

## 目录

1. [Animator Controller 资源](#1-animator-controller-资源)
2. [参数定义表](#2-参数定义表)
3. [动画层 (Layers)](#3-动画层-layers)
4. [状态机 (Base Layer)](#4-状态机-base-layer)
5. [混合树 (Blend Trees)](#5-混合树-blend-trees)
6. [核心脚本: AvatarAnimatorController](#6-核心脚本-avataranimatorcontrollercs)
7. [边缘隐藏动画: AvatarHideHandler](#7-边缘隐藏动画-avatarhidehandlercs)
8. [大屏模式: AvatarBigScreenHandler](#8-大屏模式-avatarbigscreenhandlercs)
9. [大屏计时器/闹钟: AvatarBigScreenTimer](#9-大屏计时器闹钟-avatarbigscreentimercs)
10. [屏保模式: AvatarBigScreenScreenSaver](#10-屏保模式-avatarbigscreenscreensavercs)
11. [大屏触摸: AvatarBigScreenTouchHandler](#11-大屏触摸-avatarbigscreentouchhandlercs)
12. [行走系统: AvatarLocomotionController](#12-行走系统-avatarlocomotioncontrollercs)
13. [物理摇摆: AvatarSwayController](#13-物理摇摆-avatarswaycontrollercs)
14. [睡眠系统: AvatarSleepController](#14-睡眠系统-avatarsleepcontrollercs)
15. [鼠标跟踪: AvatarMouseTracking](#15-鼠标跟踪-avatarmousetrackingcs)
16. [语音/触摸反应: PetVoiceReactionHandler](#16-语音触摸反应-petvoicereactionhandlercs)
17. [气泡/坐姿: AvatarBubbleHandler](#17-气泡坐姿-avatarbubblehandlercs)
18. [自定义舞蹈: AvatarDancePlayer](#18-自定义舞蹈-avatardanceplayercs)
19. [舞蹈安全区: AvatarDanceSafetyZone](#19-舞蹈安全区-avatardancesafetyzonecs)
20. [窗口坐姿: AvatarWindowHandler (macOS 存根)](#20-窗口坐姿-avatarwindowhandlercs)
21. [任务栏: AvatarTaskbarController (macOS 存根)](#21-任务栏-avatartaskbarcontrollercs)
22. [Q版模式: ChibiToggle](#22-q版模式-chibitogglecs)
23. [食物系统: AvatarFoodController](#23-食物系统-avatarfoodcontrollercs)
24. [BlendTree 自动循环: BlendTreeLooper](#24-blendtree-自动循环-blendtreeloopercs)
25. [其他辅助脚本](#25-其他辅助脚本)
26. [动画片段清单](#26-动画片段清单)
27. [参数写入/读取映射总表](#27-参数写入读取映射总表)
28. [状态机流转总图](#28-状态机流转总图)

---

## 1. Animator Controller 资源

### 项目主控制器

| 文件 | 用途 |
|------|------|
| `Assets/MATE ENGINE - Animations/AvatarAnimatorController.controller` | V1 主控制器 |
| `Assets/MATE ENGINE - Animations/AvatarAnimatorControllerV2.controller` | V2 主控制器（当前使用，包含更多状态） |
| `Assets/MATE ENGINE - Animations/AvatarAnimatorControllerV2 1.controller` | V2 副本 |

### 自定义舞蹈控制器

| 文件 | 用途 |
|------|------|
| `Assets/MATE ENGINE - Custom Dance Player/Prefab/CustomDanceAvatarController.controller` | 自定义舞蹈专用控制器 |
| `Assets/MATE ENGINE - Mod SDK/DanceModExample/CustomDanceAvatarController.controller` | Mod SDK 舞蹈示例 |
| `Assets/MATE ENGINE - Mod SDK/DanceModExample/DanceModExampleController.controller` | Mod SDK 控制器示例 |

### 赋值方式

- `VRMLoader.cs` 持有 `RuntimeAnimatorController animatorController` 字段
- VRM 模型加载后，将此控制器赋值给模型的 Animator 组件
- `AvatarDancePlayer` 运行时创建 `AnimatorOverrideController`，替换占位片段 `CUSTOM_DANCE`

---

## 2. 参数定义表

从 `AvatarAnimatorControllerV2.controller` 提取。Unity 类型编码: 1=Float, 4=Bool。

| 参数名 | 类型 | 默认值 | 功能说明 |
|--------|------|--------|----------|
| `isIdle` | Bool | false | 当前是否处于 Idle 状态 |
| `isDragging` | Bool | false | 是否正在拖拽角色 |
| `isDancing` | Bool | false | 是否正在跳舞（内置舞蹈） |
| `IdleIndex` | Float | 0 | Idle 混合树索引，控制播放哪个待机动画 |
| `DanceIndex` | Float | 0 | Dance 混合树索引，控制播放哪个舞蹈动画 |
| `Blend` | Float | 0 | 遗留参数（脚本中未使用） |
| `HoverTrigger` | Bool | false | 鼠标悬停在身体区域时触发身体反应 |
| `HoverFaceTrigger` | Bool | false | 鼠标悬停时触发面部反应 |
| `isSitting` | Bool | false | 是否坐下（气泡坐姿） |
| `isWindowSit` | Bool | false | 是否坐在窗口上（Windows 专用，macOS 存根） |
| `WindowSitIndex` | Float | 0 | 窗口坐姿混合树索引 |
| `isBigScreen` | Bool | false | 是否进入大屏模式 |
| `isBigScreenSaver` | Bool | false | 是否进入屏保模式 |
| `isBigScreenAlarm` | Bool | false | 是否进入闹钟模式 |
| `BigScreenBlend` | Float | 0 | 大屏混合树参数 |
| `IsSleeping` | Bool | false | 是否在睡觉 |
| `isTalking` | Bool | false | 是否在说话（LLM/对话系统驱动） |
| `isMale` | Float | 0 | 男性模式权重（0 或 1） |
| `isFemale` | Float | 1 | 女性模式权重（0 或 1） |
| `isCustomDancing` | Bool | false | 是否在播放自定义舞蹈 |
| `isWaitingForDancing` | Bool | false | 是否在等待自定义舞蹈开始 |
| `Headpat` | Bool | false | 摸头反应 |
| `IntimeRegion` | Bool | false | 敏感区域反应 |
| `HairStroke` | Bool | false | 摸头发反应（大屏模式） |
| `FaceLoop` | Bool | true | 面部待机循环是否激活 |
| `isHide` | Bool | false | 是否处于边缘隐藏状态 |
| `WalkLeft` | Bool | false | 向左行走 |
| `WalkRight` | Bool | false | 向右行走 |

### 脚本中的哈希缓存

`AvatarAnimatorController.cs` 使用 `Animator.StringToHash()` 预缓存关键参数:

```csharp
static readonly int danceIndexParam = Animator.StringToHash("DanceIndex");
static readonly int isIdleParam     = Animator.StringToHash("isIdle");
static readonly int isDraggingParam = Animator.StringToHash("isDragging");
static readonly int isDancingParam  = Animator.StringToHash("isDancing");
static readonly int idleIndexParam  = Animator.StringToHash("IdleIndex");
static readonly int isMaleParam     = Animator.StringToHash("isMale");
static readonly int isFemaleParam   = Animator.StringToHash("isFemale");
```

---

## 3. 动画层 (Layers)

| 层名 | 索引 | 功能 |
|------|------|------|
| **Base Layer** | 0 | 全身动画：Idle、Drag、Dance、Sitting、BigScreen、ScreenSaver、Alarm、Sleeping、Talk、Intro、Walk、Hide 等 |
| **Face Layer** | 1 | 面部表情动画：FaceLoop、HoverFace、Headpat、IntimeRegion、HairStroke 等 |
| **Speak Layer** | 2 | 说话口型动画，由 `isTalking` 驱动 |
| **Dance Layer** | (V2) | 自定义舞蹈层，状态名 `Custom Dance`，由 `AvatarDancePlayer` 通过 `animator.GetLayerIndex("Dance Layer")` 查找 |

---

## 4. 状态机 (Base Layer)

### 所有状态

| 状态名 | 进入条件 | 退出条件 | 说明 |
|--------|----------|----------|------|
| **Idle** | 默认入口 / 其他状态条件为 false | 任意高优先级条件为 true | 待机状态，内含 BlendTree |
| **Drag** | `isDragging == true` | `isDragging == false` | 拖拽角色时播放 |
| **Dance** | `isDancing == true` | `isDancing == false` | 内置舞蹈，BlendTree 驱动 |
| **Sitting** | `isSitting == true` | `isSitting == false` | 坐下姿势 |
| **WindowSit** | `isWindowSit == true` | `isWindowSit == false` | 坐在窗口上（macOS 无效） |
| **Big Screen** | `isBigScreen == true` | `isBigScreen == false` | 大屏模式动画 |
| **Screen Saver** | `isBigScreenSaver == true` | `isBigScreenSaver == false` | 屏保动画 |
| **Alarm** | `isBigScreenAlarm == true` | `isBigScreenAlarm == false` | 闹钟动画 |
| **Sleeping** | `IsSleeping == true` | `IsSleeping == false` | 睡觉动画 |
| **Talk / PET_TALKING** | `isTalking == true` | `isTalking == false` | 说话动画 |
| **Intro** | 初始播放 | 播放完毕 | 登场动画 |
| **HoverReaction** | `HoverTrigger == true` 且 `isIdle == true` | `HoverTrigger == false` | 鼠标悬停身体反应 |
| **Hide** | `isHide == true` | `isHide == false` | 边缘隐藏姿势 (V2) |
| **Wait For Dance** | `isWaitingForDancing == true` | `isCustomDancing == true` | 等待自定义舞蹈 (V2) |
| **Custom Dance** | `isCustomDancing == true` | `isCustomDancing == false` | 自定义舞蹈播放 (V2) |
| **Fear / Happy / Angry / Cry** | 情绪触发 | 条件结束 | 情绪动画 |
| **Eat / Drink** | 食物系统触发 | 条件结束 | 进食动画 |

### Face Layer 状态

| 状态名 | 进入条件 | 说明 |
|--------|----------|------|
| **Faceloop / Face Loop** | `FaceLoop == true`（默认） | 面部待机循环 |
| **HoverFace** | `HoverFaceTrigger == true` | 鼠标悬停面部反应 |
| **Headpat** | `Headpat == true` | 摸头面部表情 |
| **IntimeRegion** | `IntimeRegion == true` | 敏感区域面部表情 |
| **HairStroke** | `HairStroke == true` | 摸头发面部表情 (V2) |
| **None** | 空状态 | 无面部动画 |

---

## 5. 混合树 (Blend Trees)

### 5.1 Idle BlendTree

- **驱动参数**: `IdleIndex` (Float)
- **混合类型**: 1D
- **子节点数**: 约 22 个待机动画 (PET_IDLE ~ PET_IDLE_22)
- **Husbando 模式**: 通过 `isMale`/`isFemale` float 在男女动画集之间切换 (HUS_IDLE01~09)
- **切换逻辑**: `AvatarAnimatorController` 每隔 `IDLE_SWITCH_TIME`(默认 12s）递增 `idleState`，通过 `SmoothIdleTransition` 协程在 `IDLE_TRANSITION_TIME`（默认 3s）内 Lerp 过渡

### 5.2 Dance BlendTree

- **驱动参数**: `DanceIndex` (Float)
- **混合类型**: 1D
- **子节点数**: 约 14 个舞蹈动画 (PET_DANCING ~ PET_DANCING_13) + Husbando 变体 (HUS_DANCE_01~04)
- **切换逻辑**: 每隔 `DANCE_SWITCH_TIME`（默认 15s）递增 `danceState`，通过 `SmoothDanceTransition` 协程在 `DANCE_TRANSITION_TIME`（默认 2s）内 Lerp 过渡
- **初始选择**: `StartDancing()` 随机选取初始 `danceState`

### 5.3 WindowSit BlendTree

- **驱动参数**: `WindowSitIndex` (Float)
- **混合类型**: 1D
- **子节点数**: 4 个窗口坐姿 (`totalWindowSitAnimations = 4`)

### 5.4 BigScreen BlendTree

- **驱动参数**: `BigScreenBlend` (Float)
- **混合类型**: 1D

### 5.5 Male/Female BlendTree (V2)

- **驱动参数**: `isMale` 和 `isFemale` (Float)
- **功能**: 在 Idle、Dance、Drag 等状态内部，根据性别权重在男女动画集之间混合

---

## 6. 核心脚本: AvatarAnimatorController.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarAnimatorController.cs`

### 公开字段

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `animator` | Animator | null | 动画器引用 |
| `SOUND_THRESHOLD` | float | 0.02f | 音频阈值（NAudio 已移除，保留字段） |
| `allowedApps` | List\<string\> | — | 允许的应用列表（NAudio 已移除，保留字段） |
| `totalIdleAnimations` | int | 10 | Idle BlendTree 中的待机动画数量 |
| `IDLE_SWITCH_TIME` | float | 12f | 待机动画切换间隔（秒） |
| `IDLE_TRANSITION_TIME` | float | 3f | 待机动画过渡时间（秒） |
| `DANCE_CLIP_COUNT` | int | 5 | Dance BlendTree 中的舞蹈动画数量 |
| `enableDancing` | bool | true | 是否启用自动跳舞 |
| `enableDanceSwitch` | bool | true | 是否自动切换舞蹈片段 |
| `DANCE_SWITCH_TIME` | float | 15f | 舞蹈片段切换间隔（秒） |
| `DANCE_TRANSITION_TIME` | float | 2f | 舞蹈片段过渡时间（秒） |
| `BlockDraggingOverride` | bool | false | 外部拖拽阻止标志（BigScreen 使用） |
| `enableHusbandoMode` | bool | false | 男性模式开关 |
| `isDragging` | bool | — | 当前拖拽状态（公开可读） |
| `isDancing` | bool | — | 当前跳舞状态（公开可读） |
| `isIdle` | bool | — | 当前待机状态（公开可读） |

### OnEnable()

1. 如果 `animator` 为 null，自动从组件获取
2. 设置 `Application.runInBackground = true`
3. 根据 `enableHusbandoMode` 设置 `isFemale`/`isMale` float 参数

### Update() — 主循环

**每帧执行顺序:**

1. **性别参数同步**: 每帧设置 `isFemale = enableHusbandoMode ? 0 : 1`，`isMale = enableHusbandoMode ? 1 : 0`

2. **阻止检查**: 如果 `BlockDraggingOverride`、`MenuActions.IsMovementBlocked()` 或 `TutorialMenu.IsActive` 为 true:
   - 强制 `isDragging = false`，`isDancing = false`
   - 同步参数到 Animator
   - 直接 return，跳过后续逻辑

3. **拖拽检测**:
   - **鼠标按下** (`Input.GetMouseButtonDown(0)`):
     - `isDragging = true`
     - 启动 0.30s 拖拽锁定计时器 (`dragLockTime`)
     - `isDancing = false`
     - 同步 `isDraggingParam` 和 `isDancingParam` 到 Animator
   - **鼠标抬起** (`Input.GetMouseButtonUp(0)`):
     - `mouseHeld = false`
   - **拖拽锁定计时器**:
     - `dragLockTimer -= Time.deltaTime`
     - 当计时器 ≤ 0 且 `mouseHeld == false`:
       - `isDragging = false`
       - 同步 `isDraggingParam`

4. **待机动画切换**:
   - `idleTimer += Time.deltaTime`
   - 当 `idleTimer > IDLE_SWITCH_TIME`:
     - 重置 `idleTimer`
     - `idleState = (idleState + 1) % totalIdleAnimations`
     - 如果 `idleState == 0`: 直接设置 `IdleIndex = 0`（无过渡，回到第一个）
     - 否则: 启动 `SmoothIdleTransition(idleState)` 协程

5. **Idle 状态同步**: 调用 `UpdateIdleStatus()`

6. **舞蹈片段切换**:
   - 仅当 `isDancing && enableDanceSwitch` 时执行
   - `danceTimer += Time.deltaTime`
   - 当 `danceTimer > DANCE_SWITCH_TIME`:
     - 重置 `danceTimer`
     - `danceState = (danceState + 1) % DANCE_CLIP_COUNT`
     - 启动 `SmoothDanceTransition(danceState)` 协程

### StartDancing()

```
isDancing = true
danceState = Random.Range(0, DANCE_CLIP_COUNT)
animator.SetBool(isDancingParam, true)
animator.SetFloat(danceIndexParam, danceState)
```

### SetDancing(bool value)

```
isDancing = value
if (!value): 停止 SmoothDanceTransition 协程
```

### SetDragging(bool value)

```
isDragging = value
animator.SetBool(isDraggingParam, value)
```

### UpdateIdleStatus()

```
检查 animator.GetCurrentAnimatorStateInfo(0).IsName("Idle")
同步 isIdle 字段和 isIdleParam 参数
```

### SmoothIdleTransition(int newIdle) — 协程

```
elapsed = 0
startValue = animator.GetFloat(idleIndexParam)
while (elapsed < IDLE_TRANSITION_TIME):
    elapsed += Time.deltaTime
    t = elapsed / IDLE_TRANSITION_TIME
    animator.SetFloat(idleIndexParam, Mathf.Lerp(startValue, newIdle, t))
    yield return null
animator.SetFloat(idleIndexParam, newIdle)  // 确保精确值
```

### SmoothDanceTransition(int newDance) — 协程

逻辑与 `SmoothIdleTransition` 相同，操作 `danceIndexParam`，持续 `DANCE_TRANSITION_TIME`。

### OnDisable() / OnDestroy()

停止所有过渡协程。

---

## 7. 边缘隐藏动画: AvatarHideHandler.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarHideHandler.cs`

### Animator 交互

- **写入**: `animator.SetBool("isHide", true/false)`
- **读取**: `controller.isDragging`（拖拽检测）
- **骨骼获取**: `animator.GetBoneTransform(HumanBodyBones.LeftHand)`, `RightHand`（用于手部屏幕边缘检测）

### 行为流程

1. **Start()**: 获取 Animator 和 AvatarAnimatorController 引用，获取左右手骨骼 Transform
2. **拖拽时吸附**: 当 `controller.isDragging == true` 时，检测窗口位置是否靠近屏幕边缘
3. **SnapTo()**: 吸附到边缘时 `animator.SetBool("isHide", true)`
4. **Unsnap()**: 取消吸附时 `animator.SetBool("isHide", false)`，恢复 topmost 设置
5. **吸附后光标靠近**: 平滑滑出，设置 `isHide = false`（探头）
6. **光标远离**: 平滑滑回，设置 `isHide = true`（缩回）

---

## 8. 大屏模式: AvatarBigScreenHandler.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarBigScreenHandler.cs`

### Animator 交互

- **写入**: `avatarAnimator.SetBool("isBigScreen", true/false)`
- **读取**: `avatarAnimator.GetBoneTransform(attachBone)`（获取头部骨骼用于相机跟随）
- **联动**: 设置 `AvatarAnimatorController.BlockDraggingOverride = true/false`

### 行为流程

1. **ActivateBigScreen()**:
   - `avatarAnimator.SetBool("isBigScreen", true)`
   - `BlockDraggingOverride = true`（阻止拖拽）
   - 保存当前窗口位置和大小
   - 窗口扩展到全屏
   - 启动 `BigScreenEnterSequence` 协程（相机平滑推进到头部）

2. **DeactivateBigScreen()**（通过 FadeCameraY 淡出触发）:
   - `avatarAnimator.SetBool("isBigScreen", false)`
   - `BlockDraggingOverride = false`
   - 恢复窗口位置和大小
   - 启动 `BigScreenExitSequence` 协程（相机平滑拉回）

3. **相机跟随**: `FadeCameraY` 协程持续将相机对准 `attachBone`（头部骨骼）Transform 的位置

---

## 9. 大屏计时器/闹钟: AvatarBigScreenTimer.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarBigScreenTimer.cs`

### Animator 交互

- **写入**:
  - `isBigScreen = true/false`
  - `isBigScreenAlarm = true/false`
  - `isBigScreenSaver = false`（闹钟时关闭屏保）
  - `isWindowSit = false`（闹钟时退出窗口坐姿）
  - `isSitting = false`（闹钟时退出坐姿）
- **读取**: `avatarAnimator.GetCurrentAnimatorStateInfo(0).IsName(s)`（白名单状态检查）

### 行为流程

1. **TriggerAlarmNow()**:
   - 清除冲突状态: `isBigScreenSaver=false`, `isWindowSit=false`, `isSitting=false`
   - 进入大屏闹钟: `isBigScreen=true`, `isBigScreenAlarm=true`

2. **用户交互取消**:
   - 检测到鼠标/键盘输入: `isBigScreenAlarm = false`
   - 可选同时退出大屏: `isBigScreen = false`

3. **IsInAllowedState()**: 检查当前 Base Layer 状态是否在允许的白名单中

---

## 10. 屏保模式: AvatarBigScreenScreenSaver.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarBigScreenScreenSaver.cs`

### Animator 交互

- **写入**: `isBigScreen = true/false`, `isBigScreenSaver = true/false`
- **读取**: `isBigScreenAlarm`（避免打断闹钟），`GetCurrentAnimatorStateInfo(0)` 白名单检查

### 行为流程

1. **空闲计时**: 监控鼠标位置变化和键盘输入
2. **超时触发**: `isBigScreen = true`, `isBigScreenSaver = true`
3. **用户交互恢复**: `isBigScreenSaver = false`, `isBigScreen = false`
4. **保护**: 如果 `isBigScreenAlarm == true`，不会覆盖闹钟模式

---

## 11. 大屏触摸: AvatarBigScreenTouchHandler.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarBigScreenTouchHandler.cs`

### Animator 交互

- **写入**: `animator.SetBool(HairStrokeHash, true/false)`（`HairStrokeHash = Animator.StringToHash("HairStroke")`）
- **读取**: `avatarAnimator.GetBoneTransform(bigScreenHandler.attachBone)`（获取头部深度信息）

### 行为流程

1. **激活条件**: 大屏模式 + 鼠标按住
2. **按住时**: `HairStroke = true`，在鼠标位置创建物理碰撞体用于 VRM SpringBone 交互
3. **释放时**: `HairStroke = false`，移除碰撞体

---

## 12. 行走系统: AvatarLocomotionController.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarLocomotionController.cs`

### Animator 交互

- **写入**: `animator.SetBool("WalkLeft", true/false)`, `animator.SetBool("WalkRight", true/false)`
- **读取**: `animator.GetCurrentAnimatorStateInfo(baseLayerIndex).IsName("Idle")`（仅在 Idle 时可行走）

### 配置

- `BaseLayerName = "Base Layer"`
- `BaseIdleStateName = "Idle"`
- 可配置行走参数名（默认 `"WalkLeft"`, `"WalkRight"`）

### Animator 发现策略

`ResolveAnimatorSmart()` 按优先级查找 Animator:
1. 自身组件的 Animator
2. `AvatarAnimatorController.animator`
3. `PetVoiceReactionHandler.avatarAnimator`
4. `AvatarBubbleHandler.avatarAnimator`
5. 场景中任意活跃的 Animator

### 行为流程

1. **IsBaseIdle()**: 检查 Base Layer 当前状态是否为 `"Idle"`
2. **StartWalk()**: 根据角色屏幕位置选择方向，设置 `WalkLeft` 或 `WalkRight = true`
3. **StepWalk()**: 每帧移动窗口位置，实现角色在桌面上行走
4. **StopWalking()**: 设置 `WalkLeft = false`, `WalkRight = false`
5. **碰撞检测**: 到达屏幕边缘时停止行走

---

## 13. 物理摇摆: AvatarSwayController.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarSwayController.cs`

### Animator 交互

- **读取**:
  - `anim.GetBool(draggingHash)`（`"isDragging"` 的哈希值）
  - `anim.GetBool(windowSitHash)`（`"isWindowSit"` 的哈希值）
  - `GetCurrentAnimatorStateInfo()` 白名单检查

### 骨骼获取

- Hips, LeftUpperArm, RightUpperArm, LeftUpperLeg, RightUpperLeg

### 行为流程

1. **激活条件**: `isDragging == true` 且 `isWindowSit == false` 且当前状态在允许列表中
2. **LateUpdate()**: 在 Animator 写入骨骼后，叠加弹簧物理旋转到 Hips 和四肢
3. **物理模型**: 基于窗口拖拽速度的弹簧阻尼系统

---

## 14. 睡眠系统: AvatarSleepController.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarSleepController.cs`

### Animator 交互

- **写入**: `animator.SetBool(isSleepingParam, true/false)`（`isSleepingParam = Animator.StringToHash("IsSleeping")`）
- **读取**: `animator.GetBool(b)`（检查可配置的 `wakeUpBools`，默认 `["isDragging"]`）

### 行为流程

1. **空闲计时**: 当角色处于允许的状态时累计空闲时间
2. **入睡**: 超时后 `SetSleeping(true)`
3. **唤醒**: 当任一 `wakeUpBools` 为 true 时 `SetSleeping(false)`

---

## 15. 鼠标跟踪: AvatarMouseTracking.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarMouseTracking.cs`

### Animator 交互

- **读取**:
  - `GetCurrentAnimatorStateInfo(0)` — 当前状态
  - `GetNextAnimatorStateInfo(0)` — 下一状态（过渡中）
  - `IsInTransition(0)` — 是否正在过渡
  - `animator.GetBool(t.stateOrParameterName)` — 参数级权限检查
- **骨骼获取**: Head, Spine, Chest, UpperChest, LeftEye, RightEye

### 行为流程

1. **IsAllowed()**: 检查权限列表（状态白名单 + 参数条件）
2. **LateUpdate()**: 在 Animator 更新后，叠加旋转:
   - 头部: 朝向鼠标
   - 脊柱链: 部分跟随
   - 眼睛: 独立跟随鼠标

---

## 16. 语音/触摸反应: PetVoiceReactionHandler.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/PetVoiceReactionHandler.cs`

### Animator 交互

- **写入**:
  - `avatarAnimator.SetBool(region.hoverAnimationParameter, true/false)` — 身体区域动画参数
  - `avatarAnimator.CrossFadeInFixedTime(region.hoverAnimationState, 0.1f, region.hoverLayerIndex)` — 状态级触发
  - `avatarAnimator.SetBool(region.faceAnimationParameter, true/false)` — 面部区域参数
  - `avatarAnimator.CrossFadeInFixedTime(region.faceAnimationState, 0.1f, region.faceLayerIndex)` — 面部状态级触发
- **读取**:
  - `avatarAnimator.GetBool("isCustomDancing")` — 自定义舞蹈时跳过反应
  - `avatarAnimator.GetFloat(isMaleHash)` — 性别过滤
  - `avatarAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash` — 状态白名单

### 区域系统

每个身体区域（可配置）包含:
- `targetBone`: HumanBodyBones 枚举，用于 `GetBoneTransform()`
- `hoverAnimationParameter`: Bool 参数名（如 `"HoverTrigger"`）
- `hoverAnimationState`: 状态名（如 `"HoverReaction"`），用于 CrossFade
- `hoverLayerIndex`: 目标层索引
- `faceAnimationParameter`: 面部 Bool 参数名（如 `"HoverFaceTrigger"`）
- `faceAnimationState`: 面部状态名
- `faceLayerIndex`: 面部层索引

### 行为流程

1. **射线检测**: 鼠标射线与角色碰撞体相交
2. **区域匹配**: 根据碰撞点找到最近的骨骼区域
3. **TriggerAnim()**: 设置对应区域的身体参数和面部参数
4. **ResetAfterDance()**: 舞蹈结束后重置所有区域参数为 false

---

## 17. 气泡/坐姿: AvatarBubbleHandler.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarBubbleHandler.cs`

### Animator 交互

- **写入**: `animator.SetBool(animatorParameter, true/false)`（默认 `animatorParameter = "isSitting"`）
- **读取**:
  - `animator.GetBool("isDragging")` — 仅拖拽时可切换
  - `animator.GetBool("isWindowSit")` — 窗口坐姿时阻止切换
  - `animator.GetBool("isBigScreen")` — 大屏模式时强制关闭

### 行为流程

1. **快捷键按下**（拖拽状态下）: 切换 `isSitting` 参数
2. **大屏模式**: 强制 `isSitting = false`，隐藏气泡
3. **气泡物体**: 挂载到头部骨骼，跟随角色

---

## 18. 自定义舞蹈: AvatarDancePlayer.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarDancePlayer.cs`

### Animator 交互

- **写入**:
  - `animator.SetBool(customDancingParam, true/false)`（`"isCustomDancing"`）
  - `animator.SetBool(waitingParam, true/false)`（`"isWaitingForDancing"`）
  - `animator.speed = 0`（冻结动画器，用于同步）
  - `animator.speed = 1`（恢复动画器）
- **读取**:
  - `animator.GetCurrentAnimatorStateInfo(layerIndex).shortNameHash`（检查是否在舞蹈状态）
  - `animator.IsInTransition(layerIndex)`（是否在过渡中）
  - `animator.GetBool(waitingParam)`（是否在等待状态）
  - `animator.GetLayerIndex("Dance Layer")`（查找舞蹈层索引）

### AnimatorOverrideController 使用

```csharp
// 保存原始控制器
defaultController = animator.runtimeAnimatorController;

// 创建覆盖控制器
overrideController = new AnimatorOverrideController(defaultController);

// 替换占位片段
overrideController["CUSTOM_DANCE"] = loadedDanceClip;

// 应用到 Animator
animator.runtimeAnimatorController = overrideController;
```

### 行为流程

1. **加载舞蹈**: 从 AssetBundle 加载 AnimationClip
2. **进入等待**: `isWaitingForDancing = true`（Animator 过渡到 Wait For Dance 状态）
3. **替换片段**: 通过 AnimatorOverrideController 替换 `CUSTOM_DANCE` 占位符
4. **开始舞蹈**: `isCustomDancing = true`（过渡到 Custom Dance 状态）
5. **同步**: 多实例场景下通过 `animator.speed` 冻结/解冻实现帧同步
6. **结束**: `isCustomDancing = false`, `isWaitingForDancing = false`

---

## 19. 舞蹈安全区: AvatarDanceSafetyZone.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarDanceSafetyZone.cs`

### Animator 交互

- **读取**: `animator.GetBool(dancingParam)`（`"isCustomDancing"` 的哈希值）

### 行为流程

- 当 `isCustomDancing == true` 时，监控角色是否超出屏幕可见区域
- 如果超出，平移相机将角色拉回可见范围

---

## 20. 窗口坐姿: AvatarWindowHandler.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarWindowHandler.cs`

**状态**: macOS 存根（no-op）。保留公开字段以避免 Unity 序列化引用断裂。

### 原始功能（已移除）

- 检测桌面上打开的窗口顶边
- 将角色放置在窗口顶边上（坐姿）
- 使用 `isWindowSit` 和 `WindowSitIndex` 参数
- `blockSitIfBoolTrue` 字段: 参数黑名单，当这些 Bool 为 true 时阻止坐下

---

## 21. 任务栏: AvatarTaskbarController.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarTaskbarController.cs`

**状态**: macOS 存根（no-op）。保留 `avatarAnimator` 字段和 `SetAnimator()` 方法。

---

## 22. Q版模式: ChibiToggle.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/ChibiToggle.cs`

### Animator 交互

- **骨骼获取**: Hips, Head, LeftFoot, RightFoot, LeftUpperLeg, RightUpperLeg
- **不设置任何参数**
- 通过缩放骨骼 Transform 实现 Q 版效果

---

## 23. 食物系统: AvatarFoodController.cs

**路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarFoodController.cs`

### Animator 交互

- **读取**: `animator.GetBool("isBigScreen")`（大屏模式下调整交互半径）
- **骨骼获取**: `animator.GetBoneTransform(HumanBodyBones.Head)`（头部位置用于食物交互距离计算）

---

## 24. BlendTree 自动循环: BlendTreeLooper.cs

**路径**: `Assets/MATE ENGINE - Scripts/Tools/BlendTreeLooper.cs`

### 类型

`StateMachineBehaviour`（挂载在 Animator 状态机状态上的行为脚本）

### Animator 交互

- **写入**: `animator.SetFloat(blendParam, wrappedValue)`（默认 `blendParam = "Index"`）

### 行为流程

- **OnStateEnter**: 重置计时器，设置 blend 参数为 0
- **OnStateUpdate**: 计时器递增；超过 `animationDuration` 后开始 Lerp 到下一个值；使用 `Mathf.Repeat` 循环

---

## 25. 其他辅助脚本

### AvatarAnimatorReceiver.cs

- **路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarAnimatorReceiver.cs`
- **功能**: 简单的 Animator 引用持有/中继。提供 `SetAnimator(Animator)` 方法
- **用途**: 设置菜单等 UI 通过此组件获取当前 Animator

### AvatarDanceShapeConverter.cs

- **路径**: `Assets/MATE ENGINE - Scripts/AvatarHandlers/AvatarDanceShapeConverter.cs`
- **功能**: 将 MMD BlendShape 动画转换为 VRM 通用 BlendShape
- **Animator 用法**: 创建代理 `Animator` 和 `PlayableGraph` 评估动画片段，提取 BlendShape 数据。不设置任何主 Animator 参数

### MenuActions.cs

- **路径**: `Assets/MATE ENGINE - Scripts/Settings/MenuActions.cs`
- **读取**: `currentAnimator.GetBool("isDragging")`（拖拽时阻止径向菜单）
- **骨骼获取**: `currentAnimator.GetBoneTransform(targetBone)`（径向菜单跟随骨骼位置）

### VRMLoader.cs

- **路径**: `Assets/MATE ENGINE - Scripts/VRMLoader/VRMLoader.cs`
- **持有**: `RuntimeAnimatorController animatorController` 字段
- **功能**: VRM 模型加载后，将主控制器赋值给模型 Animator

---

## 26. 动画片段清单

| 分类 | 路径 | 片段列表 |
|------|------|----------|
| **面部** | `PET_FACE_LAYER/` | FACE_DRAG, FACE_HAIR_STROKE, FACE_IDLE_1, FACE_INTIME, FACE_RESET, PET_HAPPY |
| **大屏** | `PET_BIG_SCREEN/` | BIG_SCREEN_01, SCREEN_SAVER_01~04 |
| **舞蹈(女)** | `PET_DANCING/` | PET_DANCING ~ PET_DANCING_13（14个） |
| **舞蹈(男)** | `PET_DANCING/ME_02/HUSBANDO/` | HUS_DANCE_01~04 |
| **隐藏** | `PET_HIDING/` | PET_HIDE, PET_HIDE 1, PET_HIDE_SHOW_LOOP_LEFT/RIGHT, TEST_HIDE_LEFT/RIGHT |
| **待机(女)** | `PET_IDLE/` | PET_IDLE ~ PET_IDLE_15, PET_IDLE_NON_1, CUSTOM_DANCE（占位） |
| **待机(ME_02)** | `PET_IDLE/ME_02/` | PET_IDLE_16~22 |
| **待机(男)** | `PET_IDLE/ME_02/HUSBANDO/` | HUS_IDLE01~09, HUS_DRAG, HUS_SITTING |
| **登场** | `PET_INTRO/` | PET_INTRO, PET_INTRO_END, PET_INTRO_LOOP, PET_INTRO_START |
| **行走** | `PET_LOCOMOTION/` | PET_WALK_LEFT, PET_WALK_RIGHT, walk_cycle_sexy_01~03_light |
| **杂项** | `PET_MISC/` | FACE_SMILE(×2), HoverFace, HoverReaction, PET_DRAGGING, PET_HAPPY, PET_LAUGHING, PET_SHY_POINT, PLACE_HOLDER_ANIMATION |
| **姿势** | `PET_POSE/` | PET_POSE_1~4 |
| **坐姿** | `PET_SITTING/` | BETA_PET_WINDOW_LAY 系列（7+） |

**总计**: 约 100+ 动画片段

---

## 27. 参数写入/读取映射总表

| 参数 | 写入者 | 读取者 |
|------|--------|--------|
| `isIdle` | AvatarAnimatorController | 状态机过渡条件 |
| `isDragging` | AvatarAnimatorController | AvatarSwayController, AvatarHideHandler, AvatarSleepController, MenuActions, AvatarBubbleHandler |
| `isDancing` | AvatarAnimatorController | 状态机过渡条件 |
| `IdleIndex` | AvatarAnimatorController | Idle BlendTree |
| `DanceIndex` | AvatarAnimatorController | Dance BlendTree |
| `isMale` | AvatarAnimatorController | PetVoiceReactionHandler, Male/Female BlendTree |
| `isFemale` | AvatarAnimatorController | Male/Female BlendTree |
| `isBigScreen` | AvatarBigScreenHandler, AvatarBigScreenTimer, AvatarBigScreenScreenSaver | AvatarBubbleHandler, AvatarFoodController, AvatarBigScreenTimer, AvatarBigScreenScreenSaver |
| `isBigScreenSaver` | AvatarBigScreenScreenSaver | AvatarBigScreenTimer |
| `isBigScreenAlarm` | AvatarBigScreenTimer | AvatarBigScreenScreenSaver |
| `isSitting` | AvatarBubbleHandler, AvatarBigScreenTimer | AvatarBubbleHandler |
| `isWindowSit` | AvatarBigScreenTimer | AvatarSwayController, AvatarBubbleHandler |
| `WindowSitIndex` | (AvatarWindowHandler — 存根) | WindowSit BlendTree |
| `IsSleeping` | AvatarSleepController | 状态机过渡条件 |
| `isTalking` | LLM/对话系统 | 状态机过渡条件 |
| `isHide` | AvatarHideHandler | 状态机过渡条件 |
| `WalkLeft` | AvatarLocomotionController | 状态机过渡条件 |
| `WalkRight` | AvatarLocomotionController | 状态机过渡条件 |
| `isCustomDancing` | AvatarDancePlayer | PetVoiceReactionHandler, AvatarDanceSafetyZone |
| `isWaitingForDancing` | AvatarDancePlayer | AvatarDancePlayer |
| `HairStroke` | AvatarBigScreenTouchHandler | Face Layer 过渡条件 |
| `Headpat` | PetVoiceReactionHandler | Face Layer 过渡条件 |
| `IntimeRegion` | PetVoiceReactionHandler | Face Layer 过渡条件 |
| `FaceLoop` | 默认 true | Face Layer 过渡条件 |
| `HoverTrigger` | PetVoiceReactionHandler | Base Layer 过渡条件 |
| `HoverFaceTrigger` | PetVoiceReactionHandler | Face Layer 过渡条件 |
| `BigScreenBlend` | 控制器内部 | BigScreen BlendTree |

---

## 28. 状态机流转总图

```
                              ┌──────────────┐
                              │    Intro     │
                              └──────┬───────┘
                                     │ (播放完毕)
                                     ▼
                    ┌─────────────────────────────────────┐
                    │              Idle                     │
                    │  (BlendTree: IdleIndex 循环切换)       │
                    └──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬──┬────┘
                       │  │  │  │  │  │  │  │  │  │  │
          isDragging   │  │  │  │  │  │  │  │  │  │  │  isHide
              ┌────────┘  │  │  │  │  │  │  │  │  │  └────────┐
              ▼           │  │  │  │  │  │  │  │  │           ▼
         ┌────────┐       │  │  │  │  │  │  │  │  │      ┌────────┐
         │  Drag  │       │  │  │  │  │  │  │  │  │      │  Hide  │
         └────────┘       │  │  │  │  │  │  │  │  │      └────────┘
                          │  │  │  │  │  │  │  │  │
         isDancing        │  │  │  │  │  │  │  │  │  WalkLeft/Right
              ┌───────────┘  │  │  │  │  │  │  │  └───────────┐
              ▼              │  │  │  │  │  │  │              ▼
         ┌────────┐          │  │  │  │  │  │  │      ┌──────────────┐
         │ Dance  │          │  │  │  │  │  │  │      │ Walk L / R   │
         │(BlendTree)│        │  │  │  │  │  │  │      └──────────────┘
         └────────┘          │  │  │  │  │  │  │
                             │  │  │  │  │  │  │
        isSitting            │  │  │  │  │  │  │  IsSleeping
              ┌──────────────┘  │  │  │  │  │  └──────────────┐
              ▼                 │  │  │  │  │                 ▼
         ┌────────┐             │  │  │  │  │          ┌──────────┐
         │Sitting │             │  │  │  │  │          │ Sleeping │
         └────────┘             │  │  │  │  │          └──────────┘
                                │  │  │  │  │
        isBigScreen             │  │  │  │  │  isTalking
              ┌─────────────────┘  │  │  │  └─────────────────┐
              ▼                    │  │  │                    ▼
         ┌────────────┐            │  │  │            ┌──────────┐
         │ Big Screen │            │  │  │            │  Talk    │
         │            │            │  │  │            └──────────┘
         │  ┌─────────┴──┐        │  │  │
         │  │ScreenSaver │        │  │  │  HoverTrigger
         │  └─────────┬──┘        │  │  └─────────────────┐
         │  ┌─────────┴──┐        │  │                    ▼
         │  │   Alarm    │        │  │           ┌──────────────┐
         │  └────────────┘        │  │           │HoverReaction │
         └────────────────        │  │           └──────────────┘
                                  │  │
        isWindowSit               │  │  isWaitingForDancing
              ┌───────────────────┘  └───────────────────┐
              ▼                                          ▼
         ┌────────────┐                        ┌──────────────────┐
         │ WindowSit  │                        │ Wait For Dance   │
         │(BlendTree) │                        └────────┬─────────┘
         └────────────┘                                 │ isCustomDancing
                                                        ▼
                                               ┌──────────────────┐
                                               │  Custom Dance    │
                                               │(OverrideController)│
                                               └──────────────────┘


Face Layer (独立运行):

    ┌────────────┐   HoverFaceTrigger   ┌────────────┐
    │  FaceLoop  │ ──────────────────── │ HoverFace  │
    │  (默认)    │                      └────────────┘
    │            │   Headpat            ┌────────────┐
    │            │ ──────────────────── │  Headpat   │
    │            │                      └────────────┘
    │            │   IntimeRegion       ┌────────────┐
    │            │ ──────────────────── │IntimeRegion│
    │            │                      └────────────┘
    │            │   HairStroke         ┌────────────┐
    │            │ ──────────────────── │ HairStroke │
    └────────────┘                      └────────────┘


Speak Layer (独立运行):

    ┌────────────┐   isTalking          ┌────────────┐
    │    None    │ ──────────────────── │  Speaking  │
    └────────────┘                      └────────────┘
```
