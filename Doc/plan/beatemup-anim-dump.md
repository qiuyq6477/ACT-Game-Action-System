# BeatEmUp 帧数据草稿（由 .anim 曲线导出）

> 状态：参考资料 · 2026-10

本文件由菜单 `Tools → ACT → 导出 BeatEmUp 帧数据草稿（.anim → 表）` 生成，**别手改**——改了 clip 就重跑。它把 `Assets/Arts/BeatEmUp/Animation` 里 41 条 clip 的判定盒窗口、盒子轨迹、动画事件翻成整数逻辑帧，供 `beatemup-port.md` 的切片 1.7 往 `Assets/Resources/GameData/Action.json` 里填行。

## 帧号怎么对账

本工程第 n 拍里先 `anim.Update(1/60)` 再把 CurrentFrame++，所以第 n 拍摆出来的姿势就是 clip 的第 n 帧——两边是【恒等】关系（0-based 对 0-based 的姿势序号，本工程拍号从 1 起算，但换动作那一拍不算）。表里的窗口是「这一段被跨过就算数」（生效帧 = min..max+1，见 `FrameRange`），因此导出的时候 **min = 抬起那一帧，max = 落下那一帧 − 2**，这样生效集合正好等于原片子里盒子开着的那几帧，一帧不多一帧不少。盒子只开一帧的情况这条公式装不下（区间语义至少生效两拍），已在各条片子的【可疑】里点名。

这个对法不是推出来的，是两边各读了一遍代码：源工程在 MonoBehaviour 的 Update 里读 `hitBox.gameObject.activeSelf`（`UnitActions.cs:344`），而 Animator 在同一帧的更早阶段求值曲线，所以第 f 个渲染帧摆出 clip 第 f 帧的姿势、也在那一帧判命中；本工程第 n 拍同样摆第 n 帧、同样在这一拍结算命中。

## 源工程的判定盒规格

源工程全场只有一只 `HitBox`，每招靠 clip 里的位置和缩放曲线把它挪到拳头或脚上——这就是本工程要加「盒子轨迹」（切片 1.5）的原因。常驻规格：
- 局部位置 (0.57, 0.48, 0)，局部缩放 (1.2342, 1, 1)，`SpriteRenderer.size` (0.64, 0.64, 0)（精灵 Assets/Arts/BeatEmUp/Sprites/UI/HUD/WhiteSquare.png）
- 换成本工程的 3D 盒子：**size = (0.7899, 0.64, 0.5)**，中心放在上面那个局部位置；厚度 0.5 米来自源工程 `targetInZRange(0.5)` 那条纵深判定，不是随手取的

## DummyHit — 1 帧，精灵 1 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## DummyIdle — 1 帧，精灵 1 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## EnemyGrabbed — 1 帧，精灵 1 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## EnemyIdle — 31 帧，精灵 2 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## EnemyPunch — 41 帧，精灵 5 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## EnemyWalk — 41 帧，精灵 5 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## FireHydrantBroken — 19 帧，精灵 7 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## HitEffect — 17 帧，精灵 5 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## PlayerBaseballAttack — 27 帧，精灵 7 张

- 判定盒窗口（表里的 inFrames）：[11,18]（源片子开着的是第 11..19 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第12拍 (0.8164, 0.7297, 0)  
- 盒子缩放：第12拍 (2.0044, 1.0692, 1)  （世界 size = 这个 × 精灵规格）
- cue：第7拍 PlaySFX(Whoosh)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 11, "max": 18} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerDeath — 65 帧，精灵 12 张

- 判定盒窗口：没有 m_IsActive 曲线
- cue：第0拍 CamShake · 第10拍 Footstep · 第11拍 ShowEffect(SmokePuffLand) · 第38拍 PlaySFX(Land)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线

## PlayerDefend — 41 帧，精灵 5 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## PlayerDefendHit — 1 帧，精灵 1 张

- 判定盒窗口：没有 m_IsActive 曲线
- cue：第0拍 ShowEffect(DefendEffect)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线

## PlayerDropWeapon — 16 帧，精灵 4 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## PlayerGrab — 12 帧，精灵 4 张

- 判定盒窗口（表里的 inFrames）：[6,9]（源片子开着的是第 6..10 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第7拍 (0.5448, 0.5985, 0)  
- 盒子缩放：第7拍 (1.1555, 1.0881, 1)  （世界 size = 这个 × 精灵规格）
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 6, "max": 9} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerGrabKick — 36 帧，精灵 8 张

- 判定盒窗口（表里的 inFrames）：[11,34]（源片子开着的是第 11..片尾 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第4拍 (0.285, 0.697, 0)  第2拍 (0, 0, 0)  
- 盒子缩放：第4拍 (1, 1, 1)  第2拍 (1.2342, 1.6111, 1)  （世界 size = 这个 × 精灵规格）
- cue：第9拍 AddForce · 第32拍 ShowEffect(SmokePuffLand)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 11, "max": 34} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerGrabPunch — 36 帧，精灵 4 张

- 判定盒窗口（表里的 inFrames）：[10,18]（源片子开着的是第 10..19 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第1拍 (0.57, 0.48, 0)  第11拍 (0.329, 0.414, 0)  
- 盒子缩放：第1拍 (1, 1, 1)  第11拍 (1, 1, 1)  （世界 size = 这个 × 精灵规格）
- cue：第2拍 AddForce
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 10, "max": 18} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerGroundKick — 26 帧，精灵 10 张

- 判定盒窗口（表里的 inFrames）：[10,17]（源片子开着的是第 10..18 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第1拍 (0.1488, 0.2792, 0)  
- 盒子缩放：第1拍 (1.6255, 1.0119, 1)  （世界 size = 这个 × 精灵规格）
- cue：第5拍 PlaySFX(Whoosh) · 第10拍 CamShake
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 10, "max": 17} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerGroundPunch — 26 帧，精灵 9 张

- 判定盒窗口（表里的 inFrames）：[10,17]（源片子开着的是第 10..18 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第1拍 (0.2259, 0.1591, 0)  
- 盒子缩放：第1拍 (1.4556, 0.7903, 1)  （世界 size = 这个 × 精灵规格）
- cue：第5拍 PlaySFX(Whoosh) · 第10拍 CamShake
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 10, "max": 17} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerHit — 16 帧，精灵 4 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## PlayerIdle — 23 帧，精灵 5 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## PlayerJump — 31 帧，精灵 3 张

- 判定盒窗口：没有 m_IsActive 曲线
- cue：第0拍 ShowEffect(SmokePuffJump)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线

## PlayerJumpAttackKick — 30 帧，精灵 5 张

- 判定盒窗口（表里的 inFrames）：[7,14]（源片子开着的是第 7..15 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第8拍 (0.5037, 0.2485, 0)  
- 盒子缩放：第8拍 (1.2898, 3.2768, 1)  （世界 size = 这个 × 精灵规格）
- cue：第0拍 PlaySFX(Whoosh)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 7, "max": 14} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerJumpAttackPunch — 26 帧，精灵 5 张

- 判定盒窗口（表里的 inFrames）：[7,13]（源片子开着的是第 7..14 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第8拍 (0.5037, 0.2485, 0)  
- 盒子缩放：第8拍 (1.2898, 3.2768, 1)  （世界 size = 这个 × 精灵规格）
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 7, "max": 13} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerKick1 — 18 帧，精灵 6 张

- 判定盒窗口（表里的 inFrames）：[2,10]（源片子开着的是第 2..11 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第3拍 (0.354, 0.686, 0)  
- 盒子缩放：第3拍 (1.1531, 1.0991, 1)  （世界 size = 这个 × 精灵规格）
- cue：第0拍 PlaySFX(Whoosh)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 2, "max": 10} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerKick2 — 36 帧，精灵 8 张

- 判定盒窗口（表里的 inFrames）：[9,13]（源片子开着的是第 9..14 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第10拍 (0.423, 0.779, 0)  
- 盒子缩放：第10拍 (1.1531, 1.0991, 1)  （世界 size = 这个 × 精灵规格）
- cue：第0拍 AddForce · 第1拍 PlaySFX(Whoosh)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 9, "max": 13} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerKick3 — 21 帧，精灵 7 张

- 判定盒窗口（表里的 inFrames）：[4,11]（源片子开着的是第 4..12 帧）
- cue：第0拍 PlaySFX(Whoosh)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 4, "max": 11} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerKnockDownGrounded — 21 帧，精灵 2 张

- 判定盒窗口：没有 m_IsActive 曲线
- 盒子轨迹（局部位置，本工程的拍号）：第1拍 (0.027, 0.216, 0)  
- 盒子缩放：第1拍 (1, 1, 1)  （世界 size = 这个 × 精灵规格）
- cue：第0拍 ShowEffect(SmokePuffLand)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 备注：源片子里盒子全程关着（m_IsActive 一直是 0）：这条动作本来就打不着人（比如倒地中的起身前那几帧），表里就不给它 attackPhase，不是数据缺失

## PlayerKnockDown_Down — 1 帧，精灵 1 张

- 判定盒窗口（表里的 inFrames）：[0,0]（源片子开着的是第 0..片尾 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第1拍 (0.027, 0.546, 0)  
- 盒子缩放：第1拍 (1, 1, 1)  （世界 size = 这个 × 精灵规格）
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 0, "max": 0} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerKnockDown_Up — 1 帧，精灵 1 张

- 判定盒窗口：没有 m_IsActive 曲线
- 盒子轨迹（局部位置，本工程的拍号）：第1拍 (0.027, 0.546, 0)  
- 盒子缩放：第1拍 (1, 1, 1)  （世界 size = 这个 × 精灵规格）
- 备注：源片子里盒子全程关着（m_IsActive 一直是 0）：这条动作本来就打不着人（比如倒地中的起身前那几帧），表里就不给它 attackPhase，不是数据缺失

## PlayerLand — 16 帧，精灵 3 张

- 判定盒窗口：没有 m_IsActive 曲线
- cue：第0拍 PlaySFX(Land) · 第1拍 CamShake · 第2拍 ShowEffect(SmokePuffLand)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线

## PlayerPickup — 16 帧，精灵 5 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## PlayerPunch1 — 18 帧，精灵 7 张

- 判定盒窗口（表里的 inFrames）：[4,8]（源片子开着的是第 4..9 帧）
- 精灵可见窗口（m_Enabled，只影响看不看得见，别当判定窗口用）：[0,15]（源片子开着的是第 0..16 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第5拍 (0.6476, 0.5548, 0)  
- 盒子缩放：第5拍 (1.4143, 1.1711, 1)  （世界 size = 这个 × 精灵规格）
- cue：第0拍 PlaySFX(Whoosh)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 4, "max": 8} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerPunch2 — 13 帧，精灵 6 张

- 判定盒窗口（表里的 inFrames）：[4,10]（源片子开着的是第 4..11 帧）
- cue：第2拍 PlaySFX(Whoosh)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 4, "max": 10} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerPunch3 — 31 帧，精灵 6 张

- 判定盒窗口（表里的 inFrames）：[6,18]（源片子开着的是第 6..19 帧）
- 盒子轨迹（局部位置，本工程的拍号）：第7拍 (0.3971, 1.0032, 0)  
- 盒子缩放：第7拍 (1.8093, 1.6336, 1)  （世界 size = 这个 × 精灵规格）
- cue：第3拍 PlaySFX(Whoosh)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线
- 可以直接抄进表里的片段（tag 是占位名 `BeatHit`，1.7 要按部位拆开）：

```
"attackPhase": [{ "inFrames": [{"min": 6, "max": 18} ], "tag": ["BeatHit"], "attackPhase": 0, "priority": 0 }]
```

## PlayerRun — 37 帧，精灵 10 张

- 判定盒窗口：没有 m_IsActive 曲线
- cue：第8拍 Footstep · 第26拍 Footstep
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线

## PlayerStandUp — 16 帧，精灵 4 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## PlayerThrow — 17 帧，精灵 4 张

- 判定盒窗口：没有 m_IsActive 曲线
- cue：第1拍 PlaySFX(Whoosh)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线

## PlayerThrowKnife — 21 帧，精灵 5 张

- 判定盒窗口：没有 m_IsActive 曲线
- cue：第1拍 PlaySFX(Whoosh) · 第2拍 SpawnProjectile(KnifeProjectile)
  派发都得走池化（切片 1.5）：源工程每个事件都 `new GameObject + AudioSource`，照搬就撞热路径 0 分配的红线

## PunchingBagHit — 29 帧，精灵 5 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## PunchingBagIdle — 1 帧，精灵 1 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

## WaterSplash — 15 帧，精灵 5 张

纯表现片子：没有判定盒曲线也没有事件。表里就是一行只有 animKey 和 durationFrames 的动作。

---

共 41 条：15 条带判定盒窗口，20 条带 cue，0 条有可疑值。

表里还缺的列（伤害、取消标签、指令、受击阶段）在源 clip 里根本没有，属于玩法决定——由切片 1.7 对照 BeatEmUp 的 `AttackData` 与状态机填，本工具不猜。
