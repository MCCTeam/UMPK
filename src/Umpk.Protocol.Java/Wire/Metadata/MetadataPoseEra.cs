using Umpk.Game.Entities;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Native pose identifiers and invalid-id behavior, selected when an entity codec is constructed.</summary>
internal sealed class MetadataPoseEra
{
    private static readonly EntityPose[] Common = [EntityPose.Standing, EntityPose.FallFlying, EntityPose.Sleeping, EntityPose.Swimming, EntityPose.SpinAttack, EntityPose.Crouching];
    private readonly EntityPose[] _byId;
    private readonly bool _standingFallback;

    public static MetadataPoseEra None { get; } = new("none", [], false);
    public static MetadataPoseEra V1_14 { get; } = new("1.14", [.. Common, EntityPose.Dying], false);
    public static MetadataPoseEra V1_17 { get; } = new("1.17", [.. Common, EntityPose.LongJumping, EntityPose.Dying], false);
    public static MetadataPoseEra V1_19 { get; } = new("1.19", [.. V1_17._byId, EntityPose.Croaking, EntityPose.UsingTongue, EntityPose.Roaring, EntityPose.Sniffing, EntityPose.Emerging, EntityPose.Digging], false);
    public static MetadataPoseEra V1_19_3 { get; } = new("1.19.3", [.. V1_17._byId, EntityPose.Croaking, EntityPose.UsingTongue, EntityPose.Sitting, EntityPose.Roaring, EntityPose.Sniffing, EntityPose.Emerging, EntityPose.Digging], false);
    public static MetadataPoseEra V1_20_3 { get; } = new("1.20.3", [.. V1_19_3._byId, EntityPose.Sliding, EntityPose.Shooting, EntityPose.Inhaling], false);
    public static MetadataPoseEra V1_20_5 { get; } = new("1.20.5", V1_20_3._byId, true);

    private MetadataPoseEra(string name, EntityPose[] byId, bool standingFallback)
    {
        Name = name;
        _byId = byId;
        _standingFallback = standingFallback;
        ShapeToken = "poses/" + WireShapeDigest.Of(byId.Select(static (pose, index) => $"{index}:{(int)pose}").Append(standingFallback ? "invalid=standing" : "invalid=reject"));
    }

    public string Name { get; }
    public string ShapeToken { get; }
    public bool IsEmpty => _byId.Length == 0;

    public EntityPose Decode(int id)
    {
        if ((uint)id < (uint)_byId.Length)
            return _byId[id];
        if (_standingFallback)
            return EntityPose.Standing;
        throw new ProtocolViolationException($"Pose id {id} is not valid for {Name} metadata.");
    }

    public int Encode(EntityPose pose, int? originalId)
    {
        if (originalId is int original)
        {
            if (Decode(original) != pose)
                throw new ProtocolViolationException("The preserved pose id does not match the metadata value.");
            return original;
        }
        int id = Array.IndexOf(_byId, pose);
        if (id < 0)
            throw new ProtocolViolationException($"Pose {pose} is not valid for {Name} metadata.");
        return id;
    }
}
