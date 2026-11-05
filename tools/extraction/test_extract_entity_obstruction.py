import unittest


class EntityObstructionTests(unittest.TestCase):
    def test_nonblocking_entities_do_not_obstruct_placement(self) -> None:
        from extract_entity_obstruction import validate_measurement

        rows = {
            "minecraft:item": {"constructor_blocks_building": False, "blocks_building": False},
            "minecraft:falling_block": {"constructor_blocks_building": False, "blocks_building": True},
            "minecraft:zombie": {"constructor_blocks_building": True, "blocks_building": True},
            "minecraft:armor_stand": {"constructor_blocks_building": True, "blocks_building": False},
        }
        self.assertEqual({"minecraft:armor_stand": 0, "minecraft:falling_block": 0, "minecraft:item": 1, "minecraft:zombie": 0}, validate_measurement(rows, set(rows)))

    def test_incomplete_or_failed_measurements_are_rejected(self) -> None:
        from extract_entity_obstruction import validate_measurement

        for raw in [{}, {"minecraft:item": {"error": "failed constructor"}}, {"minecraft:item": {"constructor_blocks_building": 0, "blocks_building": False}}, {"minecraft:item": {"constructor_blocks_building": False}}]:
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                validate_measurement(raw, {"minecraft:item"})

    def test_unknown_building_flag_writes_are_rejected(self) -> None:
        from extract_entity_obstruction import validate_writes

        validate_writes([("net/minecraft/world/entity/LivingEntity.java", "this.blocksBuilding = true;"), ("net/minecraft/world/entity/decoration/ArmorStand.java", "this.blocksBuilding = !this.isMarker();")])
        for row in [("net/minecraft/client/Foo.java", "entity.blocksBuilding = true;"), ("net/minecraft/world/entity/LivingEntity.java", "this.blocksBuilding = someFlag;"), ("net/minecraft/world/entity/monster/Boat.java", "this.blocksBuilding = true;")]:
            with self.subTest(row=row), self.assertRaises(ValueError):
                validate_writes([row])


if __name__ == "__main__":
    unittest.main()
