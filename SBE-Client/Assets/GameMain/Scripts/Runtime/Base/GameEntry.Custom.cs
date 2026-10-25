using SepCore.Battle;
using SepCore.CustomComponent;
using SepCore.Net;
using UnityEngine;

/// <summary>
/// 游戏入口。
/// </summary>
public partial class GameEntry : MonoBehaviour
{
    public static BuiltinDataComponent BuiltinData { get; private set; }

    public static LubanComponent Luban { get; private set; }

    public static SaveComponent Save { get; private set; }

    public static RandomComponent Random { get; private set; }

    public static TurnBattleComponent TurnBattle { get; private set; }

    public static RoundComponent Round { get; private set; }

    public static NetComponent Net { get; private set; }

    private static void InitCustomComponents()
    {
        BuiltinData = UnityGameFramework.Runtime.GameEntry.GetComponent<BuiltinDataComponent>();
        Luban = UnityGameFramework.Runtime.GameEntry.GetComponent<LubanComponent>();
        Save = UnityGameFramework.Runtime.GameEntry.GetComponent<SaveComponent>();
        Random = UnityGameFramework.Runtime.GameEntry.GetComponent<RandomComponent>();
        TurnBattle = UnityGameFramework.Runtime.GameEntry.GetComponent<TurnBattleComponent>();
        Round = UnityGameFramework.Runtime.GameEntry.GetComponent<RoundComponent>();
        if (Round == null)
        {
            GameObject runGo = new GameObject("Run");
            if (TurnBattle != null && TurnBattle.transform.parent != null)
            {
                runGo.transform.SetParent(TurnBattle.transform.parent);
            }
            Round = runGo.AddComponent<RoundComponent>();
        }

        Net = UnityGameFramework.Runtime.GameEntry.GetComponent<NetComponent>();
        if (Net == null)
        {
            GameObject netGo = new GameObject("Net");
            if (Round != null)
            {
                netGo.transform.SetParent(Round.transform.parent);
            }
            Net = netGo.AddComponent<NetComponent>();
        }
    }
}
