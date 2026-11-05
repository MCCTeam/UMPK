import unittest


class EntityLifecycleAuditTests(unittest.TestCase):
    def test_dimension_guard_and_both_replacement_forms_are_resolved(self) -> None:
        from audit_entity_lifecycle import respawn_guard, replacement

        for prefix in ("", "boolean changed = next != previous;"):
            condition = "next != previous" if not prefix else "changed"
            source = f"public void handleRespawn(Packet packet) {{ {prefix} if ({condition}) {{ this.level = new ClientLevel(this); }} }}"
            self.assertEqual("next != previous", respawn_guard(source, "ClientLevel")["condition"])
        direct = "public void addEntity(Entity entity) { this.removeEntity(entity.getId(), Reason.DISCARDED); this.entityStorage.addEntity(entity); }"
        self.assertEqual("remove_by_id", replacement(direct)["kind"])
        separate_id = "private void addEntity(int id, Entity entity) { this.removeEntity(id, Reason.DISCARDED); this.entityStorage.addEntity(entity); }"
        self.assertEqual("remove_by_id", replacement(separate_id)["kind"])
        legacy = "public void add(int id, Actor incoming) { Actor old = this.lookup(id); if (old != null) { this.remove(old); } this.map.put(id, incoming); }"
        self.assertEqual("lookup_then_remove", replacement(legacy)["kind"])

    def test_unconditional_ambiguous_and_reversed_operations_are_rejected(self) -> None:
        from audit_entity_lifecycle import respawn_guard, replacement

        with self.assertRaises(ValueError):
            respawn_guard("public void handleRespawn(Packet packet) { this.level = new ClientLevel(this); }", "ClientLevel")
        with self.assertRaises(ValueError):
            respawn_guard("public void a(Packet packet) { if (a != b) { this.level = new ClientLevel(this); } } public void b(Packet packet) { if (a != b) { this.level = new ClientLevel(this); } }", "ClientLevel")
        with self.assertRaises(ValueError):
            replacement("public void addEntity(Entity entity) { this.entityStorage.addEntity(entity); this.removeEntity(entity.getId(), Reason.DISCARDED); }")


if __name__ == "__main__":
    unittest.main()
