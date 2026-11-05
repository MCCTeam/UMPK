# Native Tool component reports

The current Tool defaults for protocols 766–777 have a reproducible input path through the original vanilla server data generator. All 19 supported named releases in this range were checked. The reader was committed as `9dfeaeed5edcb4a476d14ee0f159fe776abc9286`; this report records the separate dataset provenance update. Tool values and generated runtime tables remain unchanged.

| Protocol | Releases | Registered items | Tool components | Native report format |
| --- | --- | ---: | ---: | --- |
| 766 | 1.20.5, 1.20.6 | 1330 | 33 | component-array |
| 767 | 1.21, 1.21.1 | 1333 | 33 | component-object |
| 768 | 1.21.2, 1.21.3 | 1375 | 33 | component-object |
| 769 | 1.21.4 | 1385 | 33 | component-object |
| 770 | 1.21.5 | 1396 | 33 | component-object |
| 771 | 1.21.6 | 1415 | 33 | component-object |
| 772 | 1.21.7, 1.21.8 | 1416 | 33 | component-object |
| 773 | 1.21.9, 1.21.10 | 1488 | 38 | component-object |
| 774 | 1.21.11 | 1505 | 38 | component-object |
| 775 | 26.1, 26.1.1, 26.1.2 | 1506 | 38 | per-registry-component-files |
| 776 | 26.2 | 1537 | 38 | per-registry-component-files |
| 777 | 26.3 | 1658 | 38 | per-registry-component-files |

Original server JVMs produced all 19 report sets without forced termination. Eighteen fresh whole runs passed; the retained 1.20.5 JVM exited successfully but its original wrapper failed because it looked one directory above the reports. That whole-run failure remains recorded. A separate strict offline admission verified the original native outputs, class origins, completed providers and cleanup without rerunning that JVM. All owned processes exited, and the largest observed memory peak was 659,902,464 bytes.

The source cohort binds the original report, item initialization and Tool codec owners to official artifacts. The independent review rehashed 475 matching client/server source pairs across the 19 releases. Execution used the server classes; client equivalence is source-backed. The three native report formats preserve complete item coverage, rule order, direct block selectors, tag names, optional fields and numeric types. Empty component-file omissions are accepted only with complete native production and an exact report manifest. Seven same-protocol alias comparisons passed, with no semantic differences from the retained Tool defaults.

The new report origin replaces only `_provenance.default_components` in twelve `block-mining.json` files, together with 498 JSON object-key ordering changes. Independent field comparison found no other key, value, type, floating-point bit or array-order changes. The old twelve distinct `ItemInteractionDump` raw payloads remain unavailable, and their absence has no established cause. The new reports have their own provenance and do not represent recovered old payloads.

Validation passed: 24 focused Python tests, including 21 deliberate additions; all 409 extraction tests with zero skips; dataset verification; language verification; and fresh generation with all 61 paths and bytes identical to the current generated files. A fresh reconstruction through the applied reader reproduced all 1,133 data files byte for byte, including the twelve Tool sections. The generation run compiled no producer library and retained all 57 generator runtime files unchanged; all four owned process identities were gone afterward.

Raw evidence remains outside Git under `/home/anon/Projects/MinecraftOfficial/runs/`. The native union is `component-tool-native19-lane3-union-20261105-v1/ACTUAL-NATIVE-TOOL19.json` (`0d5d6708e7ead250fd642655280ca97bfaf1ceadfe421f53c9c90ff79994dda6`). The reviewed recipe is `component-tool-report-origin-recipe-20261105-v1/NATIVE-REPORTS-RECIPE.json` (`24c2437f2ab62ed0f583b408e7f3c759fc24375c6f59e7a8c1e26a4290ee841e`). The dataset/language/generation receipts are in `tool-origin-candidate-generation61-20261105-v1/`, with independent acceptance `83b11923` and cleanup `f6d42a9b`. The strict reconstruction receipt is `ACTUAL-STRICT-RECONSTRUCTION.json` (`79e31ac62d4e4864bbe3a480b79ff89e317d4f2f19575da32b1ecf3322d3d612`) in the selected native-report CLI evidence archive indexed by the current handover. Exact commands, source pins, prior failures and binary manifests are retained with these receipts.

This closes the report-origin reconstruction of default Tool components. Other missing extraction inputs, mining controller behavior and the complete interaction acceptance ledger remain separate work.
