using System;

namespace UnityEngine.Rendering.Universal
{
    public enum TonemappingMode
    {
        None,
        Neutral, // Neutral tonemapper
        ACES,    // ACES Filmic reference tonemapper (custom approximation)
        ACESSimpleVer,  // ACES Filmic(�������Ƚ��͵ļ򵥰汾)
        GranTurismo,    // GTTonemapping(���������ɫ���仯��ͨ�����ڿ�ͨ��Ⱦ) ref:https://forum.unity.com/threads/how-to-do-custom-tone-mapping-instead-of-neutral-aces-in-urp.849280/
    }

    [Serializable, VolumeComponentMenu("Post-processing/Tonemapping")]
    public sealed class Tonemapping : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Select a tonemapping algorithm to use for the color grading process.")]
        public TonemappingModeParameter mode = new TonemappingModeParameter(TonemappingMode.None);

        public bool IsActive() => mode.value != TonemappingMode.None;

        public bool IsTileCompatible() => true;
    }

    [Serializable]
    public sealed class TonemappingModeParameter : VolumeParameter<TonemappingMode> { public TonemappingModeParameter(TonemappingMode value, bool overrideState = false) : base(value, overrideState) { } }
}
