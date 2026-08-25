using System.Collections.Generic;
using CUE4Parse.UE4.Assets.Exports.Animation;

namespace CUE4Parse_Conversion.Writers.ActorX.Structs.Animations;

/// <summary>
/// TODO: refactor
/// </summary>
public class CAnimSet
{
    public readonly USkeleton Skeleton;
    public readonly UAnimationAsset? SourceAsset;
    public readonly List<CAnimSequence> Sequences = [];

    public float TotalAnimTime;

    public CAnimSet(USkeleton skeleton, UAnimationAsset? sourceAsset = null)
    {
        Skeleton = skeleton;
        SourceAsset = sourceAsset;
    }
}
