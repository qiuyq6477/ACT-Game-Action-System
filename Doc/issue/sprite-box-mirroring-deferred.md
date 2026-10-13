# 判定盒烘培与线框还不支持 2D 精灵角色（镜像是反射，不是旋转）

> 状态：未处理 · 2026-10

解决的问题：`Action Timeline` 窗口（`Doc/plan/action-editor.md` 说的那个）
能沿动画逐帧烘出判定盒的位姿、并在 Scene 视图画线框，但它只认**3D 骨骼**。清版/横版那种"精灵贴在 3D 平面上"
的角色，烘出来的盒在角色反手时会跑到错误的一侧。现在工程里还没有精灵角色，所以这条不影响任何现有功能。

## 为什么会错

源工程的朝向是一个 yaw 角（90°/270°）。烘出来的位姿是**prefab 根节点的局部系**，运行期靠
`ActionBoxPose.ToWorldObb(unitPos, rootDeg, pitch)`（`Assets/Editor/Action/ActionBoxSampler.cs:40`）
把这个局部系按单位的朝向**旋**回世界。旋转保手性，所以这一套对 3D 模型是自洽的。

本工程把朝向落地抽到了 `FacingApply`（`Assets/Scripts/Character/FacingApply.cs:37`），它有两种后端：

- `ModelYaw`：绕 Y 转 90/270 ⇒ 是旋转 ⇒ 上面那个公式直接成立（`Player.prefab` 用的就是这个，`backend: 0`）。
- `SpriteScaleX`：**把 `localScale.x` 取反**镜像 ⇒ 这是**反射，不是旋转**。

关键在烘培的取样时机：`ActionBoxBake.Run` 克隆 prefab 后把根节点摆成 identity（`:74`
`SetPositionAndRotation(zero, identity)`），然后逐帧 `AnimationMode.SampleAnimationClip`。
`FacingApply` 的镜像是**运行期**才写的（`ActionController.cs:398` 在换动作那一拍调 `Apply`），
编辑期克隆体身上从来没有过它。所以：

1. 烘出来的那份局部位姿**只有"不镜像"的那一种**；角色反手时，运行期要在这个局部系上先做一次反射
   （`x` 分量取负 + 基向量行列式变号），而现在恢复世界位姿的唯一路径是"乘一个 yaw"，压根没有反射这条路。
2. 更糟的是这条错误是静默的：如果作者为了让镜像进数据而手动把 prefab 的 `localScale.x` 设成 -1 再烘，
   `Akku.Offer` 取的是矩阵列向量的 `magnitude`（`:265`），负缩放出来的长度仍是正数，
   于是 `degenerate`（`:266`，只防零缩放）不报警，符号被归一化悄悄吃掉（`:266` 之后 `x / lx`），
   烘出来的盒**朝左朝右是同一份数据**，看着合理、实际反手时盒留在错误一侧。

`FacingApply.cs:21-26` 早就写过同一件事的另一半：之所以翻 `localScale.x` 而不用 `SpriteRenderer.flipX`，
是因为"flipX 只翻贴图不翻子节点，出招的盒子会留在错误的一侧"。这句批评对这套烘培同样成立。

## 另外两件一起缺的事

1. **工程里一个 `SpriteRenderer` 都没有**（`Player.prefab` 是 5 个 `SkinnedMeshRenderer`，全工程 grep `!u!212` 为 0），
   也没有任何 2D 碰撞体与 2D 层。真要支持精灵角色，得先有一个能当测试对象的精灵 prefab。
2. **盒的形状从哪来**。`Doc/decision/0001-帧数据真源在表不在动画.md:20` 早就写了预期规则——
   `Tools/ACT/动画转表` 读 "`HitBox` 贴图的 `sprite.bounds × localScale` → 3D 盒子尺寸"。
   这条工具今天不存在。现在 `UnitActionBox` 只有 `size`+`offset`（`offset` 是移植时为对齐现有胶囊的
   `m_Center {y:1}` 加的），精灵角色得由这条规则填它，而不是让人目测。

## 要修该怎么修

按依赖顺序，三小步，不要一次做完：

1. 先做一个最小的精灵角色 prefab（一个 `SpriteRenderer` + 几个挂 `UnitActionBox` 的子节点），
   让"精灵"从形容词变成能跑的东西。
2. 烘培侧把镜像显式化：克隆体摆姿势时按 `FacingApply.backend` 决定要不要在局部系上再乘一次
   `Matrix4x4.Scale(new Vector3(-1,1,1))`，并把"链上有负缩放"从 `degenerate` 里单独分出来报警
   （现在它被当成正常缩放吃了进去）。
3. 线框侧加一层**屏幕空间矩形**叠加（面向正交摄像机画 quad，尺寸按 `sprite.bounds × localScale`），
   这样作者从镜头视角就能看懂左右拳——本工程摄像机是正交的（`size 2.5`、俯角 25°，`GameMain.cs:116-124`），
   直接看 3D OBB 线框不容易读出"盖住哪一格车道"。

第 2 步是正确性问题，第 3 步是可读性问题，可以分开提交。
