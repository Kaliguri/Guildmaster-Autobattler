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
        /// <summary>Что снимать: полный лист состояний или лестницу громкости фона.</summary>
        public enum Job
        {
            ContactSheet,
            ColourLadder,
            LightnessLadder,
        }

        private Job _job;

        /// <summary>Снимает заказанное и уничтожает себя.</summary>
        public void Begin(Job job = Job.ContactSheet)
        {
            _job = job;
            RunAsync().Forget();
        }

        private async UniTaskVoid RunAsync()
        {
            try
            {
                if (_job == Job.ColourLadder) await UiColourLadder.Capture(this);
                else if (_job == Job.LightnessLadder) await UiColourLadder.CaptureLightness(this);
                else await UiContactSheet.Capture(this);
            }
            finally
            {
                Destroy(gameObject);
            }
        }
    }
}
