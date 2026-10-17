import unittest


class EntityPacketBaseAuditTests(unittest.TestCase):
    def test_legacy_fields_are_distinct_and_teleport_resets_them(self):
        from audit_entity_packet_bases import inspect_listener
        source = '''public void a(Move p) { Entity e = world.get(p.id()); e.xp=e.xp+p.x(); e.yp=e.yp+p.y(); e.zp=e.zp+p.z(); }
public void b(Teleport p) { Entity e=world.get(p.id()); e.xp=p.x(); e.yp=p.y(); e.zp=p.z(); if (Math.abs(e.x-p.x()) < 0.03125 && Math.abs(e.y-p.y()) < 0.015625) {} }'''
        result = inspect_listener(source, mapped=False)
        self.assertEqual(['xp', 'yp', 'zp'], result['fields'])
        self.assertTrue(result['rotation_uses_packet_base'])
        self.assertTrue(result['teleport_resets_packet_base'])

    def test_vector_cache_requires_a_position_branch(self):
        from audit_entity_packet_bases import inspect_listener
        source = '''public void handleMoveEntity(Move p) { if (p.hasPosition()) { Vec3 v=p.updateEntityPosition(e.getPacketCoordinates()); e.setPacketCoordinates(v); } else if(p.hasRotation()) { e.lerpTo(e.getX(),e.getY(),e.getZ(),0,0,3,false); } }
public void handleTeleportEntity(Teleport p) { e.setPacketCoordinates(p.x(),p.y(),p.z()); }'''
        result = inspect_listener(source, mapped=True)
        self.assertFalse(result['rotation_uses_packet_base'])
        self.assertTrue(result['teleport_resets_packet_base'])

    def test_modern_teleport_does_not_replace_the_position_sync_base(self):
        from audit_entity_packet_bases import inspect_listener
        source = '''public void handleMoveEntity(Move p) { if(p.hasPosition()) { Vec3 v=e.getPositionCodec().decode(p.x(),p.y(),p.z()); e.getPositionCodec().setBase(v); } }
public void handleTeleportEntity(Teleport p) { setValuesFromPositionPacket(p.change(),p.relatives(),e,true); }
private static boolean setValuesFromPositionPacket(Change c, Set r, Entity e, boolean interpolate) { e.setPos(c.position()); return false; }
public void handleEntityPositionSync(Sync p) { e.getPositionCodec().setBase(p.position()); }'''
        result = inspect_listener(source, mapped=True)
        self.assertFalse(result['teleport_resets_packet_base'])
        self.assertTrue(result['sync_resets_packet_base'])
        with self.assertRaises(ValueError):
            inspect_listener(source.replace('e.setPos(c.position());', 'e.getPositionCodec().setBase(c.position());'), mapped=True)

    def test_missing_or_ambiguous_cache_ownership_is_rejected(self):
        from audit_entity_packet_bases import inspect_listener
        with self.assertRaises(ValueError):
            inspect_listener('public void handleMoveEntity(Move p) { e.setPos(p.x(),p.y(),p.z()); }', mapped=True)
        source='public void a(M p) { E e=w.get(); e.a=e.a+p.a(); e.b=e.b+p.b(); e.c=e.c+p.c(); }'
        with self.assertRaises(ValueError):
            inspect_listener(source+source, mapped=False)

    def test_obfuscated_static_packet_cache_setter_is_bound(self):
        from pathlib import Path
        from tempfile import TemporaryDirectory
        from audit_entity_packet_bases import resolve_fixed_setter
        with TemporaryDirectory() as temporary:
            root=Path(temporary)
            (root/'Cache.java').write_text('public static void seed(Entity e, double x, double y, double z) { e.a=floor(x); e.b=floor(y); e.c=floor(z); }')
            teleport={'source':'public void move(Packet p) { Entity target=world.get(); Cache.seed(target, p.x(), p.y(), p.z()); }'}
            result=resolve_fixed_setter(root,teleport,'Entity',['a','b','c'])
            self.assertEqual('Cache.java',result['file'])
            with self.assertRaises(ValueError):
                resolve_fixed_setter(root,teleport,'Entity',['a','b','missing'])
