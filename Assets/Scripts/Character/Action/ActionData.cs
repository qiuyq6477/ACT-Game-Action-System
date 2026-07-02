using System;
using System.Collections.Generic;

[Serializable]
public struct PercentageRangeData
{
    public float min;
    public float max;
}

[Serializable]
public struct CancelTagData
{
    public string tag;
    public float startFromPercentage;
    public float fadeInPercentage;
    public int priority;
}

[Serializable]
public struct BeCancelledTagData
{
    public PercentageRangeData percentageRange;
    public string[] cancelTag;
    public float fadeOutPercentage;
    public int priority;
}

[Serializable]
public struct TempBeCancelledTagData
{
    public string id;
    public float percentage;
    public string[] cancelTag;
    public float fadeOutPercentage;
    public int priority;
}

[Serializable]
public struct MoveInputAcceptanceData
{
    public PercentageRangeData range;
    public float rate;
}

[Serializable]
public struct AttackBoxTurnOnInfoData
{
    public PercentageRangeData[] inPercentage;
    public string[] tag;
    public int attackPhase;
    public int priority;
}

[Serializable]
public struct BeHitBoxTurnOnInfoData
{
    public PercentageRangeData[] inPercentage;
    public string[] tag;
    public int priority;
    public string[] tempBeCancelledTagTurnOn;
    public ActionChangeInfo attackerActionChange;
    public ActionChangeInfo selfActionChange;
}

[Serializable]
public struct ActionData
{
    public string id;
    public string animKey;
    public string catalog;
    public CancelTagData[] cancelTag;
    public BeCancelledTagData[] beCancelledTag;
    public TempBeCancelledTagData[] tempBeCancelledTag;
    public ActionCommand[] commands;
    public MoveInputAcceptanceData[] inputAcceptance;
    public string autoNextActionId;
    public bool keepPlayingAnim;
    public bool autoTerminate;
    public AttackInfo[] attacks;
    public AttackBoxTurnOnInfoData[] attackPhase;
    public BeHitBoxTurnOnInfoData[] defensePhase;
    public ScriptMethodInfo rootMotionTween;
    public int priority;
    public bool flip;
}

[Serializable]
public struct ActionDataContainer
{
    public ActionData[] data;
}
