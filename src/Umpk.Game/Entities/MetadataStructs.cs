using Umpk.Geometry;

namespace Umpk.Game.Entities;

/// <summary>Three-float rotation: pitch, yaw, and roll.</summary>
/// <param name="X">Rotation about the X axis, in degrees.</param>
/// <param name="Y">Rotation about the Y axis, in degrees.</param>
/// <param name="Z">Rotation about the Z axis, in degrees.</param>
public readonly record struct Rotations(float X, float Y, float Z);

/// <summary>A quaternion (four floats), the shape of the display-entity left/right rotation serializer.</summary>
/// <param name="X">The i component.</param>
/// <param name="Y">The j component.</param>
/// <param name="Z">The k component.</param>
/// <param name="W">The scalar component.</param>
public readonly record struct Quaternion(float X, float Y, float Z, float W);

/// <summary>Villager data: type, profession, and level.</summary>
/// <param name="Type">The villager biome-type network id.</param>
/// <param name="Profession">The villager profession network id.</param>
/// <param name="Level">The trade level, from 1 to 5.</param>
public readonly record struct VillagerData(int Type, int Profession, int Level);

/// <summary>A dimension-qualified block position.</summary>
/// <param name="Dimension">The dimension identifier this position lives in.</param>
/// <param name="Position">The block position.</param>
public readonly record struct GlobalPosition(Identifier Dimension, BlockPos Position);

/// <summary>One profile property: name, value, and optional signature.</summary>
/// <param name="Name">The property name (for example <c>textures</c>).</param>
/// <param name="Value">The base64 property value.</param>
/// <param name="Signature">The optional signature, or <see langword="null"/> when unsigned.</param>
public readonly record struct ProfileProperty(string Name, string Value, string? Signature);

/// <summary>A player-skin patch with optional client-asset identifiers and an optional slim-model flag. A <see langword="null"/> field means the patch does not override it.</summary>
/// <param name="Body">The body texture asset id, or null.</param>
/// <param name="Cape">The cape texture asset id, or null.</param>
/// <param name="Elytra">The elytra texture asset id, or null.</param>
/// <param name="SlimModel">The model override: true for the slim model, false for wide, null for no override.</param>
public readonly record struct ResolvableSkinPatch(Identifier? Body, Identifier? Cape, Identifier? Elytra, bool? SlimModel);

/// <summary>A resolvable player profile: either a resolved game profile (both <see cref="Id"/> and <see cref="Name"/> present) or a partial one (either may be absent), its authlib properties, and a <see cref="ResolvableSkinPatch"/>.</summary>
/// <param name="IsResolved">True when the wire carried a full game profile (the <c>either</c> left branch); false for a partial profile.</param>
/// <param name="Id">The profile UUID, or null when a partial profile omits it.</param>
/// <param name="Name">The profile name, or null when a partial profile omits it.</param>
/// <param name="Properties">The authlib profile properties, in wire order.</param>
/// <param name="Skin">The player-skin patch.</param>
public sealed record ResolvableProfile(
    bool IsResolved,
    Guid? Id,
    string? Name,
    IReadOnlyList<ProfileProperty> Properties,
    ResolvableSkinPatch Skin);
