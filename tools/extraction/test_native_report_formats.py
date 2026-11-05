"""Finite original-report format and unavailable-input regression cases; no native runs."""
import unittest
from extract_item_interactions import extract_tool_values

REG={'minecraft:item':{'entries':{'minecraft:stick':{},'minecraft:wooden_pickaxe':{}}},'minecraft:block':{'entries':{'minecraft:stone':{}}}}
TOOL={'rules':[{'blocks':'#minecraft:mineable/pickaxe','speed':2.0,'correct_for_drops':True},{'blocks':['minecraft:stone']}],'default_mining_speed':1.0}
class ReportFormats(unittest.TestCase):
    def test_object_retains_tool_and_known_absence(self):
        items={'minecraft:stick':{'components':{}},'minecraft:wooden_pickaxe':{'components':{'minecraft:tool':TOOL}}}
        self.assertEqual(extract_tool_values(items,REG,shape='component-object'),{'minecraft:stick':None,'minecraft:wooden_pickaxe':TOOL})
    def test_array_retains_tool_and_known_absence(self):
        items={'minecraft:stick':{'components':[]},'minecraft:wooden_pickaxe':{'components':[{'type':'minecraft:tool','value':TOOL}]}}
        self.assertEqual(extract_tool_values(items,REG,shape='component-array'),{'minecraft:stick':None,'minecraft:wooden_pickaxe':TOOL})
    def test_complete_registry_files_prove_intentional_empty_omission(self):
        self.assertEqual(extract_tool_values({'minecraft:wooden_pickaxe':{'components':{'minecraft:tool':TOOL}}},REG,shape='per-registry-component-files',complete_report=True),{'minecraft:stick':None,'minecraft:wooden_pickaxe':TOOL})
    def test_missing_completion_is_unknown_even_with_registry(self):
        with self.assertRaisesRegex(ValueError,'complete native producer'):
            extract_tool_values({},REG,shape='per-registry-component-files')
    def test_partial_object_is_unknown(self):
        with self.assertRaisesRegex(ValueError,'exact native item registry'):
            extract_tool_values({'minecraft:stick':{'components':{}}},REG,shape='component-object')
    def test_foreign_registry_file_rejected(self):
        with self.assertRaisesRegex(ValueError,'foreign'):
            extract_tool_values({'minecraft:unknown':{'components':{}}},REG,shape='per-registry-component-files',complete_report=True)
    def test_component_removal_not_full_initialized_map(self):
        with self.assertRaisesRegex(ValueError,'removal'):
            extract_tool_values({'minecraft:wooden_pickaxe':{'components':{'!minecraft:tool':{}}}},REG,shape='per-registry-component-files',complete_report=True)
    def test_missing_components_not_null(self):
        with self.assertRaisesRegex(ValueError,'component'):
            extract_tool_values({'minecraft:stick':{},'minecraft:wooden_pickaxe':{'components':{}}},REG,shape='component-object')
    def test_null_tool_not_known_absent(self):
        with self.assertRaisesRegex(ValueError,'object'):
            extract_tool_values({'minecraft:stick':{'components':{}},'minecraft:wooden_pickaxe':{'components':{'minecraft:tool':None}}},REG,shape='component-object')
    def test_duplicate_array_types_rejected(self):
        with self.assertRaisesRegex(ValueError,'duplicate'):
            extract_tool_values({'minecraft:stick':{'components':[{'type':'minecraft:other','value':1}]*2},'minecraft:wooden_pickaxe':{'components':[]}},REG,shape='component-array')
    def test_unknown_format_rejected(self):
        with self.assertRaisesRegex(ValueError,'format'):
            extract_tool_values({},REG,shape='unknown')
    def test_native_values_are_deep_copied_without_rewriting(self):
        items={'minecraft:stick':{'components':{}},'minecraft:wooden_pickaxe':{'components':{'minecraft:tool':TOOL}}}
        actual=extract_tool_values(items,REG,shape='component-object');self.assertEqual(actual['minecraft:wooden_pickaxe'],TOOL);self.assertIsNot(actual['minecraft:wooden_pickaxe'],TOOL);self.assertEqual(type(actual['minecraft:wooden_pickaxe']['rules'][0]['speed']),float)
if __name__=='__main__':unittest.main()
