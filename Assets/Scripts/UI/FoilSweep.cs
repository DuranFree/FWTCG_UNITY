using System.Collections;
using UnityEngine;

namespace FWTCG.UI
{
    /// <summary>
    /// P34 — 手牌卡片全息扫光。
    /// 挂在 HandCard 子物体 FoilSweep GO 上；Start() 启动循环：
    /// 等待 2.5s → 对角线从左到右扫过 0.8s（OutQuad） → 重置 → 等待…
    /// 随卡片 Refresh 销毁重建，无需手动停止。
    /// </summary>
    public class FoilSweep : MonoBehaviour
    {
        private RectTransform _rt;

        private const float SweepDuration = 0.8f;
        private const float WaitBetween   = 2.5f;
        private const float StartX        = -90f;
        private const float EndX          =  90f;

        private void Start()
        {
            _rt = GetComponent<RectTransform>();
            _rt.anchoredPosition = new Vector2(StartX, 0f);
            StartCoroutine(SweepLoop());
        }

        private IEnumerator SweepLoop()
        {
            while (true)
            {
                _rt.anchoredPosition = new Vector2(StartX, 0f);
                yield return new WaitForSeconds(WaitBetween);

                float t = 0f;
                while (t < SweepDuration)
                {
                    t += Time.deltaTime;
                    float p     = Mathf.Clamp01(t / SweepDuration);
                    float eased = 1f - (1f - p) * (1f - p); // OutQuad
                    _rt.anchoredPosition = new Vector2(Mathf.Lerp(StartX, EndX, eased), 0f);
                    yield return null;
                }
            }
        }
    }
}
