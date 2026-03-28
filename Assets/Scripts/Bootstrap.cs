using UnityEngine;
using FWTCG.Core;
using FWTCG.UI;

/// <summary>
/// 自动启动入口 — 无需手动在场景中放置任何 GameObject。
/// [RuntimeInitializeOnLoadMethod] 在 Play 开始后自动执行。
/// </summary>
public static class Bootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStart()
    {
        // GameManager（单例，DontDestroyOnLoad 在其 Awake 里已处理）
        if (GameManager.Instance == null)
        {
            var gmGo = new GameObject("GameManager");
            gmGo.AddComponent<GameManager>();
        }

        // GameUI（依赖 GameManager.Instance，在 Start() 里绑定事件）
        var uiGo = new GameObject("GameUI");
        uiGo.AddComponent<GameUI>();
    }
}
