namespace Umpk.Game.Entities;

/// <summary>The six equipment slots an entity carries. The numeric values match the wire slot ordinals.</summary>
public enum EquipmentSlot
{
    /// <summary>Main hand.</summary>
    MainHand = 0,

    /// <summary>Off hand.</summary>
    OffHand = 1,

    /// <summary>Feet armor slot (boots).</summary>
    Feet = 2,

    /// <summary>Legs armor slot (leggings).</summary>
    Legs = 3,

    /// <summary>Chest armor slot (chestplate/elytra).</summary>
    Chest = 4,

    /// <summary>Head armor slot (helmet).</summary>
    Head = 5,

    /// <summary>Body slot (horse/wolf armor, 1.20.5+).</summary>
    Body = 6,

    /// <summary>Saddle slot (1.21.5+).</summary>
    Saddle = 7,
}
