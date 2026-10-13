namespace CombatFramework
{
    ///<summary>
    ///输入位词表（方向 8 位 + 按键 8 位，一个 int 装得下）。
    ///
    ///框架只到"位"为止：哪个物理键算哪个位、"前/后"按谁的方向换算，全在宿主的采样表里——
    ///动作模块不认识玩法，也不认识设备。方向的命名刻意用屏幕方位（米字格）而不是格斗游戏的
    ///Forward/Backward，因为"相对谁"正是宿主才有答案的问题。
    ///
    ///**位序 = 确定性契约**：配表里的名字、录像与联机会话里的 int 掩码都按这张表解读，
    ///所以只许**尾部追加**。插在中间等于让所有存量搓招序列改义、所有旧录像从第一次匹配起分叉。
    ///</summary>
    public static class ActionKeyBits{
        //方向（米字 8 向）。"没方向"不是一个位，而是"这 8 位全 0"——ACT 的 NoDirection 就是这么表达的。
        public const int Up = 1 << 0;
        public const int UpRight = 1 << 1;
        public const int Right = 1 << 2;
        public const int DownRight = 1 << 3;
        public const int Down = 1 << 4;
        public const int DownLeft = 1 << 5;
        public const int Left = 1 << 6;
        public const int UpLeft = 1 << 7;

        ///<summary>
        ///方向位全集。它同时回答两件事：①哪些位是"电平"（按住就一直写进环）、其余是"边沿"
        ///（按下那一帧才占一格）；②切换动作时只清非方向位（一次按键只兑现一次派生，但按住的方向得留着）。
        ///</summary>
        public const int DirectionalMask = Up | UpRight | Right | DownRight | Down | DownLeft | Left | UpLeft;

        //按键：编号不带语义，宿主映射表说了算
        public const int Button1 = 1 << 8;
        public const int Button2 = 1 << 9;
        public const int Button3 = 1 << 10;
        public const int Button4 = 1 << 11;
        public const int Button5 = 1 << 12;
        public const int Button6 = 1 << 13;
        public const int Button7 = 1 << 14;
        public const int Button8 = 1 << 15;

        ///<summary>全部位的名（下标即位序：`names[i] = 1<<i`）。追加只许往这张表尾巴上加。</summary>
        public static readonly string[] Names = new string[]{
            "Up", "UpRight", "Right", "DownRight", "Down", "DownLeft", "Left", "UpLeft",
            "Button1", "Button2", "Button3", "Button4", "Button5", "Button6", "Button7", "Button8",
        };

        ///<summary>词表容量（越界的位一律丢弃，别让一个野位悄悄进快照）</summary>
        public const int BitCount = 16;
        private const int AllMask = (1 << BitCount) - 1;

        ///<summary>
        ///名字 → 位（大小写不敏感，配表手写容易飘大小写）。返回 false = 词表里没这个名，
        ///调用方（构建期）必须报错而不是当成 0——当成 0 的话这条输入条件永远不成立，症状是"这招搓不出来"。
        ///</summary>
        public static bool TryBit(string name, out int bit){
            bit = 0;
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < Names.Length; i++){
                if (string.Compare(Names[i], name, System.StringComparison.OrdinalIgnoreCase) != 0) continue;
                bit = 1 << i;
                return true;
            }
            return false;
        }

        ///<summary>把任意掩码收进词表范围（未定义的位一律抹掉）</summary>
        public static int ClampMask(int mask){
            return mask & AllMask;
        }

        ///<summary>
        ///方向位展开：斜向同时满足它的两个正交（ACT 的"按住 ↑＋← 时，↑、←、↖ 三条记录同帧都在"）。
        ///展开发生在**写入环**的时候，所以匹配侧只需判交集，不必在每次比较时再推一遍邻接关系。
        ///正交不反向满足斜向：只按 ↑ 时搓不出"需要 ↖ 的那一招"。
        ///</summary>
        public static int ExpandDirection(int dir){
            int m = dir & DirectionalMask;
            if ((m & UpRight) != 0) m |= Up | Right;
            if ((m & DownRight) != 0) m |= Down | Right;
            if ((m & DownLeft) != 0) m |= Down | Left;
            if ((m & UpLeft) != 0) m |= Up | Left;
            return m;
        }
    }
}
