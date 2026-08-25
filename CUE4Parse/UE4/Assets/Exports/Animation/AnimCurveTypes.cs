using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Utils;
using CUE4Parse.UE4.Objects.Engine.Curves;
using CUE4Parse.UE4.Objects.Engine.Animation;
using CUE4Parse.UE4.Objects.UObject;

namespace CUE4Parse.UE4.Assets.Exports.Animation
{
    public enum EAnimAssetCurveFlags
    {
        AACF_NONE = 0,
        AACF_Editable = 0x00000004,
        AACF_Metadata = 0x00000010,
    }

    [StructFallback]
    public class FAnimCurveBase
    {
        public FName CurveName;
        public int CurveTypeFlags; // Should be editor only

        public FAnimCurveBase() { }

        public FAnimCurveBase(FStructFallback data)
        {
            // UE5 cooked animation assets can serialize the former CurveName as
            // an FSmartName property named "Name".  AnimSequence compressed
            // curves restore their names separately, but raw Montage curves pass
            // through this fallback constructor and otherwise become NAME_None.
            if (!data.TryGetValue(out CurveName, nameof(CurveName)))
            {
                var smartName = data.GetOrDefault<FSmartName>("Name");
                CurveName = smartName.DisplayName;
            }
            CurveTypeFlags = data.GetOrDefault<int>(nameof(CurveTypeFlags));
        }
    }

    [StructFallback]
    public class FFloatCurve : FAnimCurveBase
    {
        public FRichCurve FloatCurve;

        public FFloatCurve() { }

        public FFloatCurve(FStructFallback data) : base(data)
        {
            FloatCurve = data.GetOrDefault<FRichCurve>(nameof(FloatCurve));
        }
    }

    [StructFallback]
    public readonly struct FRawCurveTracks
    {
        public readonly FFloatCurve[]? FloatCurves;

        public FRawCurveTracks(FStructFallback data)
        {
            FloatCurves = data.GetOrDefault<FFloatCurve[]>(nameof(FloatCurves));
        }

        public FRawCurveTracks(FFloatCurve[] floatCurves)
        {
            FloatCurves = floatCurves;
        }
    }
}
