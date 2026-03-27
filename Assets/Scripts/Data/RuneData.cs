using UnityEngine;

namespace FWTCG.Data
{
    [CreateAssetMenu(fileName = "RuneData", menuName = "FWTCG/RuneData")]
    public class RuneData : ScriptableObject
    {
        [SerializeField] public RuneType runeType;
        [SerializeField] public string displayName;   // 炽烈符文 / 灵光符文 etc.
        [SerializeField] public string schName;       // 炽烈 / 灵光 etc.（符能名）
        [SerializeField] public string emoji;
        [SerializeField] public Sprite artwork;
        [SerializeField] public Color glowColor;      // 用于 Emission / UI 高亮
    }
}
