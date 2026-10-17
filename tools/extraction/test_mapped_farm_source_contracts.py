import unittest
from extract_mapped_farm_geometry import factory_contract

class FactoryContractsTests(unittest.TestCase):

    def sources(self, split=False):
        owner = 'EntityTypes' if split else 'EntityType'
        key = 'EntityTypeIds.PIG' if split else '"pig"'
        source = 'package net.minecraft.world.entity; import net.minecraft.world.entity.animal.pig.Pig; public class ' + owner + ' { public static final EntityType<Pig> PIG = register(' + key + ', EntityType.Builder.of(Pig::new, MobCategory.CREATURE)); private static EntityType<Pig> register(String id, EntityType.Builder<Pig> builder) { return Registry.register(BuiltInRegistries.ENTITY_TYPE, id, builder.build(id)); } }'
        return {'net/minecraft/world/entity/' + owner + '.java': source, 'net/minecraft/world/entity/EntityTypeIds.java': 'package net.minecraft.world.entity; public class EntityTypeIds { public static final ResourceKey<EntityType<?>> PIG = create("pig"); private static ResourceKey<EntityType<?>> create(String id) { return ResourceKey.create(Registries.ENTITY_TYPE, Identifier.withDefaultNamespace(id)); } }'}

    def test_direct_and_split_factory_sources_bind_the_same_native_role(self):
        for split in (False, True):
            with self.subTest(split=split):
                result = factory_contract('minecraft:pig', 'net.minecraft.world.entity.animal.pig.Pig', self.sources(split))
                self.assertEqual('PIG', result['field'])
                self.assertEqual('net/minecraft/world/entity/' + ('EntityTypes' if split else 'EntityType') + '.java', result['source'])

    def test_wrong_actor_keys_registry_and_constructor_are_rejected(self):
        for split in (False, True):
            for defect in ['actor', 'key', 'registry', 'constructor', 'key-source', 'duplicate']:
                with self.subTest(split=split, defect=defect):
                    sources = self.sources(split)
                    name = 'net/minecraft/world/entity/' + ('EntityTypes' if split else 'EntityType') + '.java'
                    if defect == 'actor':
                        sources[name] = sources[name].replace('import net.minecraft.world.entity.animal.pig.Pig;', 'import other.Pig;')
                    if defect == 'key':
                        sources[name] = sources[name].replace('EntityTypeIds.PIG', 'EntityTypeIds.SHEEP') if split else sources[name].replace('"pig"', '"sheep"')
                    if defect == 'registry':
                        sources[name] = sources[name].replace('BuiltInRegistries.ENTITY_TYPE', 'BuiltInRegistries.ITEM')
                    if defect == 'constructor':
                        sources[name] = sources[name].replace('Pig::new', 'Sheep::new')
                    if defect == 'key-source':
                        if split:
                            sources['net/minecraft/world/entity/EntityTypeIds.java'] = sources['net/minecraft/world/entity/EntityTypeIds.java'].replace('"pig"', '"sheep"')
                        else:
                            sources[name] = sources[name].replace('"pig"', '"sheep"')
                    if defect == 'duplicate':
                        sources[name] += ' public static final EntityType<Pig> PIG = register(' + ('EntityTypeIds.PIG' if split else '"pig"') + ', EntityType.Builder.of(Pig::new, MobCategory.CREATURE));'
                    with self.assertRaises(ValueError):
                        factory_contract('minecraft:pig', 'net.minecraft.world.entity.animal.pig.Pig', sources)

class RegistryContractsTests(unittest.TestCase):

    def test_source_named_get_id_uses_the_official_mapping_or_unobfuscated_name(self):
        from extract_mapped_farm_geometry import registry_contract
        sources = {'net/minecraft/core/Registry.java': 'public abstract int getId(T value); public abstract Key getKey(T value);', 'net/minecraft/core/DefaultedRegistry.java': 'public int getId(T value) { return super.getId(value); }'}
        raw = {'registry': {'id_owner': 'net.minecraft.core.DefaultedRegistry', 'id_method': 'getId'}}
        self.assertEqual('net/minecraft/core/DefaultedRegistry.java', registry_contract(raw, sources, None)['implementation_source'])
        mapping = 'net.minecraft.core.DefaultedRegistry -> x:\n    1:2:int getId(java.lang.Object) -> b\n'
        raw['registry']['id_method'] = 'b'
        self.assertEqual('b', registry_contract(raw, sources, mapping)['runtime_method'])

    def test_throwing_lookup_or_wrong_mapping_cannot_replace_the_selected_get_id(self):
        from extract_mapped_farm_geometry import registry_contract
        sources = {'net/minecraft/core/Registry.java': 'int getId(T value); Key getKey(T value);', 'net/minecraft/core/DefaultedRegistry.java': 'public int getId(T value) { return 0; }'}
        for method, mapping in [('getIdOrThrow', None), ('b', 'net.minecraft.core.DefaultedRegistry -> x:\n int getId(java.lang.Object) -> a\n')]:
            with self.subTest(method=method):
                with self.assertRaises(ValueError):
                    registry_contract({'registry': {'id_owner': 'net.minecraft.core.DefaultedRegistry', 'id_method': method}}, sources, mapping)
