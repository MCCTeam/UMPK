# Extraction bootstrap assets

`block-shape-bootstrap.json` is UMPK's repository-local curated fallback snapshot. It contains the collision geometry and block references needed when an official server jar cannot be driven to measure shapes directly.

Prefer fresh measurements from the official jar whenever the required artifacts and Java runtime are available. Keep the fallback's provenance explicit and do not represent it as version-specific measured data.
