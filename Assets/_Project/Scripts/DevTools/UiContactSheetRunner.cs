using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Guildmaster.DevTools
{
    /// <summary>
    /// Держатель прогона контактного листа: живёт ровно столько, сколько снимается лист.
    /// </summary>
    /// <remarks>
    /// Нужен по одной причине: снимок берётся строго в конце кадра
    /// (<c>UniTask.WaitForEndOfFrame</c> требует <see cref="MonoBehaviour"/>), а пункт меню — код
    /// редактора, у которого своего кадра нет. Объект создаётся временно и сносит себя сам, поэтому в
    /// сцене после прогона не остаётся ничего.
    /// </remarks>
    public sealed class UiContactSheetRunner : MonoBehaviour
    {
        /// <summary>Снимает лист и уничтожает себя.</summary>
        public void Begin() => RunAsync().Forget();

        private async UniTaskVoid RunAsync()
        {
            try
            {
                await UiContactSheet.Capture(this);
            }
            finally
            {
                Destroy(gameObject);
            }
        }
    }
}
