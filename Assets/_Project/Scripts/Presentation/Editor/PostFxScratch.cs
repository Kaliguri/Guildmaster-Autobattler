using UnityEngine;
using UnityEngine.Rendering;

namespace Guildmaster.Presentation.Editor
{
    /// <summary>
    /// Временная копия профиля пост-обработки — то, на чём стенды крутят настройки, чтобы не трогать
    /// сам ассет.
    /// </summary>
    /// <remarks>
    /// Живёт отдельным типом, потому что копию просят уже два стенда (Post FX Lab и витрина блума), а
    /// правка настоящего профиля ради замера пометила бы ассет изменённым — и подобранное «на
    /// посмотреть» уехало бы в игру молча.
    /// </remarks>
    public static class PostFxScratch
    {
        /// <summary>
        /// Копия профиля в памяти. Собирается по-компонентно, а не <c>Instantiate</c>: список
        /// <c>components</c> хранит ССЫЛКИ на подассеты, и копия профиля указывала бы на те же самые
        /// компоненты — правка «копии» ушла бы прямиком в ассет.
        /// </summary>
        public static VolumeProfile Clone(VolumeProfile source)
        {
            var clone = ScriptableObject.CreateInstance<VolumeProfile>();
            clone.hideFlags = HideFlags.HideAndDontSave;
            if (source == null) return clone;

            foreach (VolumeComponent component in source.components)
            {
                // Дырка в списке — не паранойя: в боевом профиле первой ссылкой лежит ровно {fileID: 0}.
                if (component == null) continue;

                var copy = (VolumeComponent)ScriptableObject.CreateInstance(component.GetType());
                copy.hideFlags = HideFlags.HideAndDontSave;
                for (int i = 0; i < component.parameters.Count; i++)
                {
                    copy.parameters[i].overrideState = component.parameters[i].overrideState;
                    copy.parameters[i].SetValue(component.parameters[i]);
                }
                clone.components.Add(copy);
            }
            return clone;
        }

        /// <summary>Убрать копию вместе с её компонентами: они самостоятельные объекты и сами не уйдут.</summary>
        public static void Destroy(VolumeProfile profile)
        {
            if (profile == null) return;
            foreach (VolumeComponent component in profile.components)
                if (component != null) Object.DestroyImmediate(component);
            Object.DestroyImmediate(profile);
        }
    }
}
