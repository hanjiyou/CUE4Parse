using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Writers.ActorX.Structs.Animations;
using CUE4Parse_Conversion.Writers.UEFormat;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Writers;

using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CUE4Parse_Conversion.Formats.Animations;

public sealed class UEFormatAnimFormat : IAnimExportFormat
{
    public string DisplayName => "UEFormat (ueanim)";

    public IReadOnlyList<ExportFile> Build(string objectName, string objectPath, ExportOptions options, CAnimSet animSet)
    {
        if (animSet.SourceAsset is UAnimMontage montage)
            return BuildMontage(objectName, objectPath, options, animSet, montage);

        var results = new List<ExportFile>(animSet.Sequences.Count);
        for (var i = 0; i < results.Capacity; i++)
        {
            using var ar = new FArchiveWriter();
            new UEAnim(objectName, objectPath, animSet, i, options).Save(ar);

            var suffix = i == 0 ? "" : $"_SEQ{i}";
            results.Add(new ExportFile("ueanim", ar.GetBuffer(), suffix));
        }

        return results;
    }

    private static IReadOnlyList<ExportFile> BuildMontage(
        string objectName,
        string objectPath,
        ExportOptions options,
        CAnimSet animSet,
        UAnimMontage montage)
    {
        var results = new List<ExportFile>();
        var payloads = new List<MontagePayload>();
        var payloadBySource = new Dictionary<string, MontagePayload>(StringComparer.OrdinalIgnoreCase);

        for (var sequenceIndex = 0; sequenceIndex < animSet.Sequences.Count; sequenceIndex++)
        {
            var sequence = animSet.Sequences[sequenceIndex];
            var sourcePath = sequence.OriginalSequence.GetPathName();
            if (payloadBySource.ContainsKey(sourcePath))
                continue;

            var payloadIndex = payloads.Count;
            var suffix = payloadIndex == 0 ? string.Empty : $"_SEQ{payloadIndex}";
            var payload = new MontagePayload(
                payloadIndex,
                sourcePath,
                sequence.OriginalSequence.Name,
                $"{objectName}{suffix}.ueanim");
            payloads.Add(payload);
            payloadBySource.Add(sourcePath, payload);

            using var ar = new FArchiveWriter();
            new UEAnim(objectName, objectPath, animSet, sequenceIndex, options).Save(ar);
            results.Add(new ExportFile("ueanim", ar.GetBuffer(), suffix));
        }

        var slots = montage.SlotAnimTracks.Select((slot, slotIndex) => new
        {
            slot_index = slotIndex,
            slot_name = slot.SlotName.Text,
            segments = slot.AnimTrack.AnimSegments.Select((segment, segmentIndex) =>
            {
                segment.AnimReference.TryLoad<UAnimSequence>(out var sourceSequence);
                var resolved = sourceSequence is not null;
                var sourcePath = sourceSequence?.GetPathName();
                var payload = sourcePath is not null && payloadBySource.TryGetValue(sourcePath, out var found)
                    ? found
                    : null;
                return new
                {
                    segment_index = segmentIndex,
                    source_animation_name = sourceSequence?.Name ?? segment.AnimReference.Name,
                    source_animation_path = sourcePath,
                    resolved,
                    payload_index = payload?.Index,
                    payload_file = payload?.FileName,
                    start_position = segment.StartPos,
                    animation_start_time = segment.AnimStartTime,
                    animation_end_time = segment.AnimEndTime,
                    animation_play_rate = segment.AnimPlayRate,
                    effective_play_rate = segment.GetValidPlayRate(),
                    looping_count = segment.LoopingCount,
                    segment_length = segment.GetLength()
                };
            }).ToArray()
        }).ToArray();

        var sidecar = new
        {
            schema = "cue4parse.montage",
            schema_version = 2,
            montage = new
            {
                object_name = objectName,
                object_path = objectPath,
                skeleton_path = animSet.Skeleton.GetPathName(),
                skeleton_guid = montage.SkeletonGuid.ToString(),
                sequence_length = montage.SequenceLength,
                calculated_sequence_length = montage.CalculateSequenceLength(),
                rate_scale = montage.RateScale,
                loop = montage.bLoop,
                blend_in = SerializePropertyValue(montage, "BlendIn"),
                blend_out = SerializePropertyValue(montage, "BlendOut"),
                blend_out_trigger_time = SerializePropertyValue(montage, "BlendOutTriggerTime")
            },
            deduplication = new
            {
                strategy = "source_animation_path",
                segment_occurrences = slots.Sum(slot => slot.segments.Length),
                unique_animation_payloads = payloads.Count
            },
            payloads = payloads.Select(payload => new
            {
                payload_index = payload.Index,
                payload_file = payload.FileName,
                source_animation_name = payload.SourceName,
                source_animation_path = payload.SourcePath
            }).ToArray(),
            sections = montage.CompositeSections.Select((section, sectionIndex) => new
            {
                section_index = sectionIndex,
                section_name = section.SectionName.Text,
                next_section_name = section.NextSectionName.Text,
                time = section.GetTime(),
                slot_index = section.SlotIndex,
                segment_index = section.SegmentIndex,
                link_method = section.LinkMethod.ToString(),
                cached_link_method = section.CachedLinkMethod.ToString(),
                segment_begin_time = section.SegmentBeginTime,
                segment_length = section.SegmentLength,
                link_value = section.LinkValue,
                linked_sequence_path = ResolveObjectPath(section.LinkedSequence),
                metadata = (section.MetaData ?? []).Select(item => item.GetPathName()).ToArray()
            }).ToArray(),
            slots,
            notifies = (montage.Notifies ?? []).Select((notify, notifyIndex) => new
            {
                notify_index = notifyIndex,
                kind = notify.NotifyStateClass is { IsNull: false } ? "notify_state" : "notify",
                notify_name = notify.NotifyName?.Text,
                time = notify.GetTime(),
                duration = notify.Duration,
                end_time = notify.EndLink?.GetTime(),
                trigger_time_offset = notify.TriggerTimeOffset,
                end_trigger_time_offset = notify.EndTriggerTimeOffset,
                slot_index = notify.SlotIndex,
                segment_index = notify.SegmentIndex,
                track_index = notify.TrackIndex,
                link_method = notify.LinkMethod.ToString(),
                cached_link_method = notify.CachedLinkMethod.ToString(),
                segment_begin_time = notify.SegmentBeginTime,
                segment_length = notify.SegmentLength,
                link_value = notify.LinkValue,
                linked_montage_path = ResolveObjectPath(notify.LinkedMontage),
                linked_sequence_path = ResolveObjectPath(notify.LinkedSequence),
                end_link = BuildLinkData(notify.EndLink),
                montage_tick_type = notify.MontageTickType.ToString(),
                trigger_chance = notify.NotifyTriggerChance,
                trigger_weight_threshold = notify.TriggerWeightThreshold,
                notify_filter_type = notify.NotifyFilterType.ToString(),
                notify_filter_lod = notify.NotifyFilterLOD,
                converted_from_branching_point = notify.bConvertedFromBranchingPoint,
                trigger_on_dedicated_server = notify.bTriggerOnDedicatedServer,
                trigger_on_follower = notify.bTriggerOnFollower,
                notify_object_path = ResolveObjectPath(notify.Notify),
                notify_state_class_path = ResolveObjectPath(notify.NotifyStateClass),
                notify_object = SerializeSemanticObject(notify.Notify),
                notify_state = SerializeSemanticObject(notify.NotifyStateClass)
            }).ToArray(),
            curves = SerializeCurves(montage.RawCurveData?.FloatCurves),
            boundaries = new[]
            {
                "Bone payloads are deduplicated by referenced AnimSequence path.",
                "Slot, segment, section, and notify semantics live in this sidecar and are not serialized into UEAnim bone tracks.",
                "UEAnim payloads contain referenced AnimSequence tracks; they are not a baked multi-slot Montage evaluation.",
                "Notify and NotifyState properties are serialized from the cooked instance; referenced external assets remain object paths.",
                "Montage curves belong to the Montage timeline and are preserved in this sidecar, not duplicated into payload UEAnim files."
            }
        };
        var json = JsonConvert.SerializeObject(sidecar, Formatting.Indented);
        results.Add(new ExportFile("json", Encoding.UTF8.GetBytes(json), ".montage"));
        return results;
    }

    private static string? ResolveObjectPath(CUE4Parse.UE4.Objects.UObject.FPackageIndex? index)
    {
        if (index is null || index.IsNull)
            return null;
        return index.TryLoad(out var value) ? value.GetPathName() : null;
    }

    private static JObject? BuildLinkData(FAnimLinkableElement? link)
    {
        if (link is null)
            return null;

        return new JObject
        {
            ["time"] = link.GetTime(),
            ["slot_index"] = link.SlotIndex,
            ["segment_index"] = link.SegmentIndex,
            ["link_method"] = link.LinkMethod.ToString(),
            ["cached_link_method"] = link.CachedLinkMethod.ToString(),
            ["segment_begin_time"] = link.SegmentBeginTime,
            ["segment_length"] = link.SegmentLength,
            ["link_value"] = link.LinkValue,
            ["linked_montage_path"] = ResolveObjectPath(link.LinkedMontage),
            ["linked_sequence_path"] = ResolveObjectPath(link.LinkedSequence)
        };
    }

    private static JToken? SerializePropertyValue(UObject value, string propertyName)
    {
        var property = value.Properties.FirstOrDefault(item =>
            item.Name.Text.Equals(propertyName, StringComparison.Ordinal));
        return property?.Tag is null
            ? null
            : JToken.FromObject(property.Tag, JsonSerializer.CreateDefault());
    }

    private static JObject? SerializeSemanticObject(FPackageIndex? index)
    {
        if (index is null || index.IsNull || !index.TryLoad(out var value))
            return null;
        return SerializeSemanticObject(value, []);
    }

    private static JObject SerializeSemanticObject(UObject value, HashSet<string> visited)
    {
        var objectPath = value.GetPathName();
        if (!visited.Add(objectPath))
        {
            return new JObject
            {
                ["object_name"] = value.Name,
                ["object_path"] = objectPath,
                ["cycle_reference"] = true
            };
        }

        var serialized = JObject.FromObject(value, JsonSerializer.CreateDefault());
        var ownedSubobjects = new JArray();
        if (value.Owner is not null)
        {
            foreach (var candidate in value.Owner.GetExports())
            {
                if (ReferenceEquals(candidate, value) || candidate.Outer?.Object is not { } outer)
                    continue;
                try
                {
                    if (outer.Value.GetPathName().Equals(objectPath, StringComparison.Ordinal))
                        ownedSubobjects.Add(SerializeSemanticObject(candidate, visited));
                }
                catch
                {
                    // A sidecar should retain the primary Notify even when an unrelated
                    // inline export cannot be resolved. The unresolved reference remains
                    // present in the serialized property bag as its object path.
                }
            }
        }

        return new JObject
        {
            ["object_name"] = value.Name,
            ["object_path"] = objectPath,
            ["export_type"] = value.ExportType,
            ["class_name"] = value.Class?.Name.Text,
            ["properties"] = serialized["Properties"]?.DeepClone() ?? new JObject(),
            ["owned_subobjects"] = ownedSubobjects
        };
    }

    private static JArray SerializeCurves(FFloatCurve[]? curves)
    {
        var result = new JArray();
        foreach (var curve in curves ?? [])
        {
            var keys = new JArray();
            foreach (var key in curve.FloatCurve.Keys)
            {
                keys.Add(new JObject
                {
                    ["time_seconds"] = key.Time,
                    ["value"] = key.Value,
                    ["interpolation_mode"] = key.InterpMode.ToString(),
                    ["tangent_mode"] = key.TangentMode.ToString(),
                    ["tangent_weight_mode"] = key.TangentWeightMode.ToString(),
                    ["arrive_tangent"] = key.ArriveTangent,
                    ["arrive_tangent_weight"] = key.ArriveTangentWeight,
                    ["leave_tangent"] = key.LeaveTangent,
                    ["leave_tangent_weight"] = key.LeaveTangentWeight
                });
            }

            result.Add(new JObject
            {
                ["curve_name"] = curve.CurveName.Text,
                ["curve_type_flags"] = curve.CurveTypeFlags,
                ["default_value"] = curve.FloatCurve.DefaultValue,
                ["pre_infinity_extrapolation"] = curve.FloatCurve.PreInfinityExtrap.ToString(),
                ["post_infinity_extrapolation"] = curve.FloatCurve.PostInfinityExtrap.ToString(),
                ["keys"] = keys
            });
        }
        return result;
    }

    private sealed record MontagePayload(
        int Index,
        string SourcePath,
        string SourceName,
        string FileName);

    public IReadOnlyList<ExportFile> BuildAnimStreamable(string objectName, string objectPath, ExportOptions options, UAnimStreamable animStreamable)
    {
        using var ar = new FArchiveWriter();
        new UEAnim(objectName, objectPath, animStreamable, options).Save(ar);
        return [new ExportFile("ueanim", ar.GetBuffer())];
    }
}
